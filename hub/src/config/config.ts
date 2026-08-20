import { readFileSync } from "node:fs";
import path from "node:path";
import { z } from "zod";

const configSchema = z.object({
  host: z.literal("127.0.0.1"),
  port: z.number().int().min(1024).max(65535),
  requestTimeout: z.number().int().min(100).max(300_000),
  maxPayloadBytes: z.number().int().min(1_024).max(16_777_216).default(1_048_576),
  logFile: z.string().min(1).default("logs/hub.log"),
}).strict();

export type HubConfig = z.infer<typeof configSchema> & {
  configPath: string;
  logFilePath: string;
};

function candidateConfigPaths(): string[] {
  const configuredPath = process.env["UNITY_CODEX_HUB_CONFIG"];
  const candidates = [
    configuredPath,
    path.resolve(process.cwd(), "config.json"),
    path.resolve(process.cwd(), "../config.json"),
  ];

  return candidates.filter((candidate): candidate is string => candidate !== undefined);
}

export function loadConfig(): HubConfig {
  let lastError: unknown;

  for (const configPath of candidateConfigPaths()) {
    try {
      const parsedJson: unknown = JSON.parse(readFileSync(configPath, "utf8"));
      const config = configSchema.parse(parsedJson);
      const rootDirectory = path.dirname(configPath);
      return {
        ...config,
        configPath,
        logFilePath: path.resolve(rootDirectory, config.logFile),
      };
    } catch (error) {
      lastError = error;
    }
  }

  const description = lastError instanceof Error ? lastError.message : String(lastError);
  throw new Error(`Unable to load Unity Codex Hub config.json: ${description}`);
}
