import { mkdtempSync, rmSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import type { HubConfig } from "../src/config/config.js";
import { Logger } from "../src/logging/logger.js";
import { PROTOCOL_VERSION } from "../src/protocol/messages.js";
import { UnityConnection } from "../src/websocket/unityConnection.js";

async function unusedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert(address !== null && typeof address !== "string");
  const port = address.port;
  await new Promise<void>((resolve, reject) => {
    server.close((error) => error === undefined ? resolve() : reject(error));
  });
  return port;
}

function waitForMessage(socket: WebSocket): Promise<Record<string, unknown>> {
  return new Promise((resolve, reject) => {
    socket.once("message", (data) => {
      try {
        resolve(JSON.parse(data.toString()) as Record<string, unknown>);
      } catch (error) {
        reject(error);
      }
    });
    socket.once("error", reject);
  });
}

test("handshake, correlation, structured errors, timeout, disconnect, and reconnect", async () => {
  const temporaryDirectory = mkdtempSync(path.join(tmpdir(), "unity-codex-hub-"));
  const port = await unusedPort();
  const config: HubConfig = {
    host: "127.0.0.1",
    port,
    requestTimeout: 100,
    maxPayloadBytes: 1_048_576,
    logFile: "hub.log",
    configPath: path.join(temporaryDirectory, "config.json"),
    logFilePath: path.join(temporaryDirectory, "hub.log"),
  };
  const connection = new UnityConnection(config, new Logger(config.logFilePath));
  await connection.start();

  const socket = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    socket.once("open", resolve);
    socket.once("error", reject);
  });

  const hubHelloPromise = waitForMessage(socket);
  socket.send(JSON.stringify({
    type: "unity_hello",
    protocolVersion: PROTOCOL_VERSION,
    unityVersion: "2022.3.62f1",
    projectName: "TestProject",
    projectPath: "/tmp/TestProject",
    currentScene: "SampleScene",
  }));
  assert.deepEqual(await hubHelloPromise, { type: "hub_hello", protocolVersion: PROTOCOL_VERSION });
  assert.equal(connection.status().connected, true);

  const invalidCandidate = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    invalidCandidate.once("open", resolve);
    invalidCandidate.once("error", reject);
  });
  const invalidClosed = new Promise<void>((resolve) => invalidCandidate.once("close", () => resolve()));
  invalidCandidate.send(JSON.stringify({
    type: "unity_hello",
    protocolVersion: PROTOCOL_VERSION + 1,
    unityVersion: "6000.5.8f1",
    projectName: "Impostor",
    projectPath: "/tmp/Impostor",
    currentScene: "None",
  }));
  await invalidClosed;
  assert.equal(connection.status().connected, true, "invalid candidate must not displace active Unity");

  const oversizedCandidate = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    oversizedCandidate.once("open", resolve);
    oversizedCandidate.once("error", reject);
  });
  const oversizedClosed = new Promise<void>((resolve) => oversizedCandidate.once("close", () => resolve()));
  oversizedCandidate.send("x".repeat(config.maxPayloadBytes + 1));
  await oversizedClosed;
  assert.equal(connection.status().connected, true, "oversized candidate must not displace active Unity");

  await assert.rejects(
    connection.execute("set_terrain_heights", { instanceId: "1", heights: "x".repeat(config.maxPayloadBytes) }),
    (error: unknown) => error instanceof Error && "code" in error && error.code === "RESULT_TOO_LARGE",
  );

  socket.once("message", (data) => {
    const request = JSON.parse(data.toString()) as { requestId: string; command: string };
    assert.equal(request.command, "get_status");
    socket.send(JSON.stringify({
      requestId: request.requestId,
      success: true,
      result: { connected: true, projectName: "TestProject" },
      error: null,
    }));
  });

  const result = await connection.execute("get_status", {});
  assert.deepEqual(result, { connected: true, projectName: "TestProject" });

  socket.once("message", (data) => {
    const request = JSON.parse(data.toString()) as { requestId: string; command: string };
    assert.equal(request.command, "get_game_object");
    socket.send(JSON.stringify({
      requestId: request.requestId,
      success: false,
      result: null,
      error: { code: "OBJECT_NOT_FOUND", message: "Object does not exist" },
    }));
  });
  await assert.rejects(
    connection.execute("get_game_object", { instanceId: 999 }),
    (error: unknown) => error instanceof Error && "code" in error && error.code === "OBJECT_NOT_FOUND",
  );

  socket.once("message", (data) => {
    const request = JSON.parse(data.toString()) as { command: string };
    assert.equal(request.command, "get_hierarchy");
    // Intentionally do not reply: the Hub must expire the request.
  });
  await assert.rejects(
    connection.execute("get_hierarchy", {}),
    (error: unknown) => error instanceof Error && "code" in error && error.code === "TIMEOUT",
  );

  socket.close();
  await new Promise<void>((resolve) => socket.once("close", resolve));
  await assert.rejects(
    connection.execute("get_hierarchy", {}),
    (error: unknown) => error instanceof Error && "code" in error && error.code === "UNITY_NOT_CONNECTED",
  );

  const reconnectedSocket = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    reconnectedSocket.once("open", resolve);
    reconnectedSocket.once("error", reject);
  });
  const reconnectHelloPromise = waitForMessage(reconnectedSocket);
  reconnectedSocket.send(JSON.stringify({
    type: "unity_hello",
    protocolVersion: PROTOCOL_VERSION,
    unityVersion: "2022.3.62f1",
    projectName: "TestProject",
    projectPath: "/tmp/TestProject",
    currentScene: "SampleScene",
  }));
  assert.deepEqual(
    await reconnectHelloPromise,
    { type: "hub_hello", protocolVersion: PROTOCOL_VERSION },
  );
  assert.equal(connection.status().connected, true);

  const replacementSocket = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    replacementSocket.once("open", resolve);
    replacementSocket.once("error", reject);
  });
  const replacementHelloPromise = waitForMessage(replacementSocket);
  const previousClosed = new Promise<void>((resolve) => reconnectedSocket.once("close", () => resolve()));
  replacementSocket.send(JSON.stringify({
    type: "unity_hello",
    protocolVersion: PROTOCOL_VERSION,
    unityVersion: "6000.5.8f1",
    projectName: "ReplacementProject",
    projectPath: "/tmp/ReplacementProject",
    currentScene: "ReplacementScene",
  }));
  assert.deepEqual(await replacementHelloPromise, { type: "hub_hello", protocolVersion: PROTOCOL_VERSION });
  await previousClosed;
  assert.equal(connection.status().projectName, "ReplacementProject");

  replacementSocket.once("message", (data) => {
    const request = JSON.parse(data.toString()) as { requestId: string; command: string };
    assert.equal(request.command, "get_status");
    replacementSocket.send(JSON.stringify({
      requestId: request.requestId,
      success: true,
      result: { connected: true },
      error: { code: "INTERNAL_ERROR", message: "invalid success envelope" },
    }));
  });
  await assert.rejects(
    connection.execute("get_status", {}),
    (error: unknown) => error instanceof Error && "code" in error && error.code === "UNITY_NOT_CONNECTED",
  );
  assert.equal(connection.status().connected, false);
  if (replacementSocket.readyState !== WebSocket.CLOSED) {
    await new Promise<void>((resolve) => replacementSocket.once("close", resolve));
  }

  await connection.stop();
  rmSync(temporaryDirectory, { recursive: true, force: true });
});
