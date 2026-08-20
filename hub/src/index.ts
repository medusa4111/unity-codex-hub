#!/usr/bin/env node
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { loadConfig } from "./config/config.js";
import { Logger } from "./logging/logger.js";
import { createMcpServer } from "./mcp/unityTools.js";
import { errorMessage } from "./protocol/error.js";
import { UnityConnection } from "./websocket/unityConnection.js";

async function main(): Promise<void> {
  const config = loadConfig();
  const logger = new Logger(config.logFilePath);
  const unityConnection = new UnityConnection(config, logger);
  const mcpServer = createMcpServer(unityConnection, logger);
  let shuttingDown = false;

  const shutdown = async (reason: string): Promise<void> => {
    if (shuttingDown) {
      return;
    }
    shuttingDown = true;
    logger.info("Unity Codex Hub stopping", { reason });
    await mcpServer.close().catch(() => undefined);
    await unityConnection.stop().catch(() => undefined);
  };

  process.once("SIGINT", () => void shutdown("SIGINT").finally(() => process.exit(0)));
  process.once("SIGTERM", () => void shutdown("SIGTERM").finally(() => process.exit(0)));
  process.stdin.once("end", () => void shutdown("stdin closed"));

  await unityConnection.start();
  const transport = new StdioServerTransport();
  await mcpServer.connect(transport);
  logger.info("MCP stdio server started", { config: config.configPath });
}

main().catch((error: unknown) => {
  process.stderr.write(`Unity Codex Hub failed to start: ${errorMessage(error)}\n`);
  process.exit(1);
});
