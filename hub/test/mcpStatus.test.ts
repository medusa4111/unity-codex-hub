import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { WebSocket } from "ws";
import { PROTOCOL_VERSION, UNITY_COMMANDS } from "../src/protocol/messages.js";

async function unusedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert(address !== null && typeof address !== "string");
  const port = address.port;
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return port;
}

async function openWebSocket(url: string): Promise<WebSocket> {
  const socket = new WebSocket(url);
  await new Promise<void>((resolve, reject) => {
    socket.once("open", resolve);
    socket.once("error", reject);
  });
  return socket;
}

test("MCP stdio completes the status, hierarchy, create, console, and save workflow", async () => {
  const temporaryDirectory = mkdtempSync(path.join(tmpdir(), "unity-codex-mcp-"));
  const port = await unusedPort();
  const configPath = path.join(temporaryDirectory, "config.json");
  writeFileSync(configPath, JSON.stringify({
    host: "127.0.0.1",
    port,
    requestTimeout: 1_000,
    maxPayloadBytes: 1_048_576,
    logFile: "hub.log",
  }), "utf8");

  const testDirectory = path.dirname(fileURLToPath(import.meta.url));
  const hubDirectory = path.resolve(testDirectory, "../..");
  const entryPoint = path.join(hubDirectory, "dist/src/index.js");
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [entryPoint],
    cwd: hubDirectory,
    env: { UNITY_CODEX_HUB_CONFIG: configPath },
    stderr: "pipe",
  });
  const client = new Client({ name: "unity-codex-hub-test", version: "1.0.0" });

  try {
    await client.connect(transport);
    const tools = await client.listTools();
    const expectedNames = new Set([
      ...UNITY_COMMANDS.map((command) => command === "get_status" ? "unity_status" : `unity_${command}`),
      "unity_wait_for_ready",
      "unity_wait_for_play_mode",
    ]);
    assert.deepEqual(new Set(tools.tools.map((tool) => tool.name)), expectedNames);
    assert.equal(tools.tools.length, expectedNames.size);
    for (const tool of tools.tools) {
      assert.equal(tool.annotations?.openWorldHint, false, `${tool.name} must be closed-world`);
      assert.equal(typeof tool.annotations?.readOnlyHint, "boolean", `${tool.name} needs readOnlyHint`);
      assert.equal(typeof tool.annotations?.destructiveHint, "boolean", `${tool.name} needs destructiveHint`);
      assert.equal(typeof tool.annotations?.idempotentHint, "boolean", `${tool.name} needs idempotentHint`);
    }

    const disconnectedResult = await client.callTool({ name: "unity_status", arguments: {} });
    assert.equal(disconnectedResult.isError, undefined);
    const disconnectedEnvelope = disconnectedResult.structuredContent as {
      success: boolean;
      result: { connected: boolean };
    };
    assert.equal(disconnectedEnvelope.success, true);
    assert.equal(disconnectedEnvelope.result.connected, false);

    const socket = await openWebSocket(`ws://127.0.0.1:${port}`);
    socket.send(JSON.stringify({
      type: "unity_hello",
      protocolVersion: PROTOCOL_VERSION,
      unityVersion: "2022.3.62f1",
      projectName: "McpProject",
      projectPath: temporaryDirectory,
      currentScene: "SampleScene",
    }));
    await new Promise<void>((resolve, reject) => {
      socket.once("message", () => resolve());
      socket.once("error", reject);
    });

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      assert.equal(request.command, "get_status");
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: {
          connected: true,
          unityVersion: "2022.3.62f1",
          projectName: "McpProject",
          projectPath: temporaryDirectory,
          currentScene: "SampleScene",
          scenePath: "Assets/Scenes/SampleScene.unity",
          playModeStatus: "stopped",
          isPlaying: false,
          isCompiling: false,
        },
        error: null,
      }));
    });

    const connectedResult = await client.callTool({ name: "unity_status", arguments: {} });
    const connectedEnvelope = connectedResult.structuredContent as {
      result: { connected: boolean; projectName: string };
    };
    assert.equal(connectedEnvelope.result.connected, true);
    assert.equal(connectedEnvelope.result.projectName, "McpProject");

    const captureDirectory = path.join(temporaryDirectory, "Library/UnityCodexBridge/Captures");
    mkdirSync(captureDirectory, { recursive: true });
    const captureFile = path.join(captureDirectory, "game-view-test.png");
    const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1]);
    writeFileSync(captureFile, png);
    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as {
        requestId: string;
        command: string;
        params: Record<string, unknown>;
      };
      assert.equal(request.command, "capture_game_view");
      assert.deepEqual(request.params, { width: 64, height: 64, transparentBackground: false });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: {
          capturePath: "Library/UnityCodexBridge/Captures/game-view-test.png",
          mimeType: "image/png",
          width: 64,
          height: 64,
        },
        error: null,
      }));
    });
    const captureResult = await client.callTool({
      name: "unity_capture_game_view",
      arguments: { width: 64, height: 64 },
    });
    const captureContent = captureResult.content as Array<{ type: string; data?: string }>;
    const imageContent = captureContent.find((item) => item.type === "image");
    assert(imageContent !== undefined && imageContent.type === "image" && imageContent.data !== undefined);
    assert.equal(Buffer.from(imageContent.data, "base64").equals(png), true);
    assert.equal(existsSync(captureFile), false);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string; params: Record<string, unknown> };
      assert.equal(request.command, "find_assets");
      assert.deepEqual(request.params, { query: "", type: "Prefab", offset: 0, limit: 10 });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { results: [{ guid: "0123456789abcdef0123456789abcdef", assetPath: "Assets/Crate.prefab" }], totalMatches: 1 },
        error: null,
      }));
    });
    const assets = await client.callTool({ name: "unity_find_assets", arguments: { type: "Prefab", limit: 10 } });
    assert.equal((assets.structuredContent as { success: boolean }).success, true);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string; params: Record<string, unknown> };
      assert.equal(request.command, "get_asset_info");
      assert.deepEqual(request.params, {
        assetPath: "Assets/Crate.prefab", includeDependencies: false, dependencyLimit: 200,
      });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { assetPath: "Assets/Crate.prefab", category: "Prefab", isPrefab: true },
        error: null,
      }));
    });
    const assetInfo = await client.callTool({ name: "unity_get_asset_info", arguments: { assetPath: "Assets/Crate.prefab" } });
    assert.equal((assetInfo.structuredContent as { success: boolean }).success, true);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string; params: Record<string, unknown> };
      assert.equal(request.command, "import_asset");
      assert.deepEqual(request.params, {
        assetPath: "Assets/Crate.prefab", forceUpdate: false, forceSynchronousImport: false,
      });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { imported: true, asset: { assetPath: "Assets/Crate.prefab" } },
        error: null,
      }));
    });
    const imported = await client.callTool({ name: "unity_import_asset", arguments: { assetPath: "Assets/Crate.prefab" } });
    assert.equal((imported.structuredContent as { success: boolean }).success, true);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string; params: Record<string, unknown> };
      assert.equal(request.command, "batch");
      assert.deepEqual(request.params, {
        operations: [{ command: "delete_game_object", instanceId: "99" }],
        stopOnError: false,
        undoGroupName: "Codex: Batch",
      });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: {
          succeeded: 0,
          failed: 1,
          stoppedEarly: false,
          results: [{ index: 0, command: "delete_game_object", success: false, error: { code: "OBJECT_NOT_FOUND", message: "missing" } }],
        },
        error: null,
      }));
    });
    const batch = await client.callTool({
      name: "unity_batch",
      arguments: { operations: [{ command: "delete_game_object", instanceId: "99" }], stopOnError: false },
    });
    const batchResult = (batch.structuredContent as { result: { results: Array<{ success: boolean }> } }).result;
    assert.equal(batchResult.results[0]?.success, false);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      assert.equal(request.command, "get_hierarchy");
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { scene: "SampleScene", rootCount: 0, objects: [] },
        error: null,
      }));
    });
    const hierarchyResult = await client.callTool({ name: "unity_get_hierarchy", arguments: {} });
    const hierarchyEnvelope = hierarchyResult.structuredContent as {
      result: { scene: string; rootCount: number };
    };
    assert.deepEqual(hierarchyEnvelope.result, { scene: "SampleScene", rootCount: 0, objects: [] });

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as {
        requestId: string;
        command: string;
        params: Record<string, unknown>;
      };
      assert.equal(request.command, "create_game_object");
      assert.deepEqual(request.params, { name: "CodexTest" });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { name: "CodexTest", instanceId: 12345, hierarchyPath: "CodexTest" },
        error: null,
      }));
    });
    const createResult = await client.callTool({
      name: "unity_create_game_object",
      arguments: { name: "CodexTest" },
    });
    const createEnvelope = createResult.structuredContent as {
      success: boolean;
      result: { name: string; instanceId: number };
    };
    assert.equal(createEnvelope.success, true);
    assert.deepEqual(createEnvelope.result, { name: "CodexTest", instanceId: 12345, hierarchyPath: "CodexTest" });

    const invalidCreate = await client.callTool({
      name: "unity_create_game_object",
      arguments: { name: "" },
    });
    assert.equal(invalidCreate.isError, true);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as {
        requestId: string;
        command: string;
        params: Record<string, unknown>;
      };
      assert.equal(request.command, "get_console");
      assert.deepEqual(request.params, {
        errorsOnly: true,
        severities: ["Error", "Warning", "Log"],
        sinceSequence: 0,
        maxResults: 200,
        includeStackTrace: true,
      });
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { errorsOnly: true, count: 0, messages: [] },
        error: null,
      }));
    });
    const consoleResult = await client.callTool({
      name: "unity_get_console",
      arguments: { errorsOnly: true },
    });
    assert.equal((consoleResult.structuredContent as { success: boolean }).success, true);

    socket.once("message", (data) => {
      const request = JSON.parse(data.toString()) as { requestId: string; command: string };
      assert.equal(request.command, "save_scene");
      socket.send(JSON.stringify({
        requestId: request.requestId,
        success: true,
        result: { success: true, scenePath: "Assets/Scenes/SampleScene.unity" },
        error: null,
      }));
    });
    const saveResult = await client.callTool({ name: "unity_save_scene", arguments: {} });
    const saveEnvelope = saveResult.structuredContent as {
      result: { success: boolean; scenePath: string };
    };
    assert.deepEqual(saveEnvelope.result, {
      success: true,
      scenePath: "Assets/Scenes/SampleScene.unity",
    });
    socket.close();
    await new Promise<void>((resolve) => socket.once("close", resolve));
  } finally {
    await client.close();
    rmSync(temporaryDirectory, { recursive: true, force: true });
  }
});
