import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { WebSocket, type RawData } from "ws";
import { PROTOCOL_VERSION } from "../src/protocol/messages.js";

test("wait tools survive compiling, Domain Reload disconnect, reconnect, and Play Mode transition", async () => {
  const temporaryDirectory = mkdtempSync(path.join(tmpdir(), "unity-codex-wait-"));
  const port = await unusedPort();
  const configPath = path.join(temporaryDirectory, "config.json");
  writeFileSync(configPath, JSON.stringify({
    host: "127.0.0.1", port, requestTimeout: 1_000, maxPayloadBytes: 1_048_576, logFile: "hub.log",
  }));
  const testDirectory = path.dirname(fileURLToPath(import.meta.url));
  const hubDirectory = path.resolve(testDirectory, "../..");
  const client = new Client({ name: "unity-wait-test", version: "1.0.0" });
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [path.join(hubDirectory, "dist/src/index.js")],
    cwd: hubDirectory,
    env: { UNITY_CODEX_HUB_CONFIG: configPath },
    stderr: "pipe",
  });
  let firstSocket: WebSocket | undefined;
  let secondSocket: WebSocket | undefined;
  try {
    await client.connect(transport);
    firstSocket = await connectUnity(port);
    const firstClosed = new Promise<void>((resolve) => firstSocket?.once("close", () => resolve()));
    firstSocket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      assert.equal(request.command, "get_status");
      firstSocket?.send(JSON.stringify(statusResponse(request.requestId, {
        isCompiling: true,
        isUpdating: false,
        isPlaying: false,
        isPaused: false,
        isPlayingOrWillChangePlaymode: false,
        playModeTransition: "none",
      })));
      setTimeout(() => firstSocket?.close(), 10);
    });

    const waitPromise = client.callTool({
      name: "unity_wait_for_ready",
      arguments: { timeoutMs: 3_000, pollIntervalMs: 50 },
    });
    await firstClosed;
    await delay(75);
    secondSocket = await connectUnity(port);
    const readyHandler = (data: RawData): void => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      assert.equal(request.command, "get_status");
      secondSocket?.send(JSON.stringify(statusResponse(request.requestId, {
        isCompiling: false,
        isUpdating: false,
        isPlaying: false,
        isPaused: false,
        isPlayingOrWillChangePlaymode: false,
        playModeTransition: "none",
      })));
    };
    secondSocket.on("message", readyHandler);
    const ready = await waitPromise;
    secondSocket.off("message", readyHandler);
    assert.equal(ready.isError, undefined);
    assert.equal((ready.structuredContent as { result: { ready: boolean } }).result.ready, true);

    secondSocket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      secondSocket?.send(JSON.stringify(statusResponse(request.requestId, {
        isCompiling: false,
        isUpdating: false,
        isPlaying: true,
        isPaused: false,
        isPlayingOrWillChangePlaymode: true,
        playModeTransition: "none",
      })));
    });
    const playing = await client.callTool({
      name: "unity_wait_for_play_mode",
      arguments: { state: "playing", timeoutMs: 2_000, pollIntervalMs: 50 },
    });
    assert.equal(playing.isError, undefined);
    assert.equal((playing.structuredContent as { result: { reached: string } }).result.reached, "playing");
  } finally {
    firstSocket?.close();
    secondSocket?.close();
    await client.close();
    rmSync(temporaryDirectory, { recursive: true, force: true });
  }
});

async function connectUnity(port: number): Promise<WebSocket> {
  const socket = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    socket.once("open", resolve);
    socket.once("error", reject);
  });
  const hello = new Promise<void>((resolve, reject) => {
    socket.once("message", () => resolve());
    socket.once("error", reject);
  });
  socket.send(JSON.stringify({
    type: "unity_hello",
    protocolVersion: PROTOCOL_VERSION,
    unityVersion: "6000.5.8f1",
    projectName: "WaitProject",
    projectPath: "/tmp/WaitProject",
    currentScene: "SampleScene",
  }));
  await hello;
  return socket;
}

function statusResponse(requestId: string, state: Record<string, unknown>): Record<string, unknown> {
  return {
    requestId,
    success: true,
    result: { connected: true, ...state },
    error: null,
  };
}

async function unusedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert(address !== null && typeof address !== "string");
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return address.port;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
