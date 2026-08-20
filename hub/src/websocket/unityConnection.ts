import { randomUUID } from "node:crypto";
import { WebSocket, WebSocketServer, type RawData } from "ws";
import type { HubConfig } from "../config/config.js";
import type { Logger } from "../logging/logger.js";
import { HubError, errorMessage } from "../protocol/error.js";
import {
  PROTOCOL_VERSION,
  type HubHello,
  type UnityCommand,
  type UnityCommandRequest,
  type UnityCommandResponse,
  type UnityHello,
  unityCommandResponseSchema,
  unityHelloSchema,
} from "../protocol/messages.js";

interface PendingRequest {
  command: UnityCommand;
  resolve: (response: UnityCommandResponse) => void;
  reject: (error: HubError) => void;
  timeout: NodeJS.Timeout;
}

export interface UnityConnectionStatus {
  connected: boolean;
  unityVersion: string | null;
  projectName: string | null;
  projectPath: string | null;
  currentScene: string | null;
}

function isLoopbackAddress(address: string | undefined): boolean {
  return address === "127.0.0.1" || address === "::1" || address === "::ffff:127.0.0.1";
}

export class UnityConnection {
  private server: WebSocketServer | undefined;
  private activeSocket: WebSocket | undefined;
  private hello: UnityHello | undefined;
  private readonly pending = new Map<string, PendingRequest>();

  constructor(
    private readonly config: HubConfig,
    private readonly logger: Logger,
  ) {}

  async start(): Promise<void> {
    if (this.server !== undefined) {
      return;
    }

    const server = new WebSocketServer({
      host: this.config.host,
      port: this.config.port,
      maxPayload: this.config.maxPayloadBytes,
      perMessageDeflate: false,
      clientTracking: false,
    });
    this.server = server;

    server.on("connection", (socket, request) => {
      if (!isLoopbackAddress(request.socket.remoteAddress)) {
        this.logger.warn("Rejected non-loopback WebSocket client", {
          remoteAddress: request.socket.remoteAddress,
        });
        socket.close(1008, "Local connections only");
        return;
      }
      this.acceptSocket(socket);
    });

    server.on("error", (error) => {
      this.logger.error("WebSocket server error", { error: error.message });
    });

    await new Promise<void>((resolve, reject) => {
      const handleListening = (): void => {
        server.off("error", handleInitialError);
        resolve();
      };
      const handleInitialError = (error: Error): void => {
        server.off("listening", handleListening);
        reject(error);
      };
      server.once("listening", handleListening);
      server.once("error", handleInitialError);
    });

    this.logger.info("Unity WebSocket listener started", {
      host: this.config.host,
      port: this.config.port,
    });
  }

  async stop(): Promise<void> {
    const server = this.server;
    this.server = undefined;
    this.disconnectActiveSocket("Hub stopping");

    if (server === undefined) {
      return;
    }

    await new Promise<void>((resolve) => {
      server.close(() => resolve());
    });
  }

  status(): UnityConnectionStatus {
    const connected = this.activeSocket?.readyState === WebSocket.OPEN && this.hello !== undefined;
    return {
      connected,
      unityVersion: this.hello?.unityVersion ?? null,
      projectName: this.hello?.projectName ?? null,
      projectPath: this.hello?.projectPath ?? null,
      currentScene: this.hello?.currentScene ?? null,
    };
  }

  async execute(command: UnityCommand, params: Record<string, unknown>): Promise<unknown> {
    const socket = this.activeSocket;
    if (socket === undefined || socket.readyState !== WebSocket.OPEN || this.hello === undefined) {
      throw new HubError("UNITY_NOT_CONNECTED", "Unity Editor is not connected to Unity Codex Hub");
    }

    const requestId = randomUUID();
    const request: UnityCommandRequest = { requestId, command, params };
    const payload = JSON.stringify(request);
    const payloadBytes = Buffer.byteLength(payload, "utf8");
    if (payloadBytes > this.config.maxPayloadBytes) {
      throw new HubError("RESULT_TOO_LARGE",
        `Unity command payload is ${payloadBytes} bytes; the configured limit is ${this.config.maxPayloadBytes}`,
        { payloadBytes, maxPayloadBytes: this.config.maxPayloadBytes });
    }
    this.logger.request(command, requestId);

    return await new Promise<unknown>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(requestId);
        this.logger.result(false, requestId, { code: "TIMEOUT" });
        reject(new HubError(
          "TIMEOUT",
          `Unity command '${command}' timed out after ${this.config.requestTimeout} ms`,
        ));
      }, this.config.requestTimeout);

      const pending: PendingRequest = {
        command,
        timeout,
        resolve: (response) => {
          if (response.success) {
            resolve(response.result);
          } else {
            reject(new HubError(response.error.code, response.error.message, response.error.details));
          }
        },
        reject,
      };
      this.pending.set(requestId, pending);

      socket.send(payload, (error) => {
        // ws invokes this callback with null at runtime even though its typings use Error | undefined.
        if (error == null) {
          return;
        }
        const current = this.pending.get(requestId);
        if (current === undefined) {
          return;
        }
        clearTimeout(current.timeout);
        this.pending.delete(requestId);
        current.reject(new HubError("UNITY_NOT_CONNECTED", `Failed to send command to Unity: ${error.message}`));
      });
    });
  }

  private acceptSocket(socket: WebSocket): void {
    let handshakeComplete = false;
    const handshakeTimeout = setTimeout(() => {
      if (!handshakeComplete) {
        socket.close(1008, "Handshake required");
      }
    }, 5_000);

    socket.on("message", (data, isBinary) => {
      if (isBinary) {
        socket.close(1003, "Text JSON messages required");
        return;
      }

      try {
        const message: unknown = JSON.parse(this.rawDataToString(data));
        if (!handshakeComplete) {
          const hello = unityHelloSchema.parse(message);
          if (hello.protocolVersion !== PROTOCOL_VERSION) {
            socket.close(1002, `Unsupported protocol version ${hello.protocolVersion}`);
            return;
          }
          handshakeComplete = true;
          clearTimeout(handshakeTimeout);
          if (this.activeSocket !== undefined && this.activeSocket !== socket) {
            this.logger.warn("Replacing existing authenticated Unity connection");
            this.disconnectActiveSocket("Unity reconnected");
          }
          this.activeSocket = socket;
          this.hello = hello;
          const response: HubHello = { type: "hub_hello", protocolVersion: PROTOCOL_VERSION };
          socket.send(JSON.stringify(response));
          this.logger.info("Unity connected", {
            project: hello.projectName,
            unityVersion: hello.unityVersion,
          });
          return;
        }

        if (this.activeSocket !== socket) {
          socket.close(1008, "Connection is no longer active");
          return;
        }
        this.handleResponse(message);
      } catch (error) {
        this.logger.warn("Rejected invalid Unity message", { error: errorMessage(error) });
        socket.close(1007, "Invalid protocol message");
      }
    });

    socket.on("close", () => {
      clearTimeout(handshakeTimeout);
      if (this.activeSocket !== socket) {
        return;
      }
      this.activeSocket = undefined;
      this.hello = undefined;
      this.rejectPending("Unity connection closed");
      this.logger.warn("Unity disconnected");
    });

    socket.on("error", (error) => {
      this.logger.warn("Unity WebSocket error", { error: error.message });
    });
  }

  private handleResponse(message: unknown): void {
    const response = unityCommandResponseSchema.parse(message) as UnityCommandResponse;
    const pending = this.pending.get(response.requestId);
    if (pending === undefined) {
      this.logger.warn("Ignoring response for unknown or expired request", { requestId: response.requestId });
      return;
    }

    clearTimeout(pending.timeout);
    this.pending.delete(response.requestId);
    this.logger.result(response.success, response.requestId, {
      command: pending.command,
      code: response.success ? undefined : response.error.code,
    });
    pending.resolve(response);
  }

  private disconnectActiveSocket(reason: string): void {
    const socket = this.activeSocket;
    this.activeSocket = undefined;
    this.hello = undefined;
    if (socket !== undefined) {
      socket.close(1001, reason);
    }
    this.rejectPending(reason);
  }

  private rejectPending(message: string): void {
    for (const [requestId, pending] of this.pending) {
      clearTimeout(pending.timeout);
      pending.reject(new HubError("UNITY_NOT_CONNECTED", message));
      this.pending.delete(requestId);
    }
  }

  private rawDataToString(data: RawData): string {
    if (Array.isArray(data)) {
      return Buffer.concat(data).toString("utf8");
    }
    if (data instanceof ArrayBuffer) {
      return Buffer.from(data).toString("utf8");
    }
    return data.toString("utf8");
  }
}
