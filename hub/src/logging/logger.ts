import { appendFileSync, mkdirSync } from "node:fs";
import path from "node:path";

type LogLevel = "INFO" | "WARN" | "ERROR" | "REQUEST" | "RESULT";

export interface LogFields {
  [key: string]: string | number | boolean | null | undefined;
}

export class Logger {
  constructor(private readonly logFilePath: string) {
    mkdirSync(path.dirname(logFilePath), { recursive: true });
  }

  info(message: string, fields?: LogFields): void {
    this.write("INFO", message, fields);
  }

  warn(message: string, fields?: LogFields): void {
    this.write("WARN", message, fields);
  }

  error(message: string, fields?: LogFields): void {
    this.write("ERROR", message, fields);
  }

  request(command: string, requestId: string): void {
    this.write("REQUEST", command, { requestId });
  }

  result(success: boolean, requestId: string, fields?: LogFields): void {
    this.write("RESULT", success ? "success" : "failure", { requestId, ...fields });
  }

  private write(level: LogLevel, message: string, fields?: LogFields): void {
    const metadata = fields === undefined
      ? ""
      : ` ${Object.entries(fields)
          .filter((entry) => entry[1] !== undefined)
          .map(([key, value]) => `${key}=${JSON.stringify(value)}`)
          .join(" ")}`;
    const line = `${new Date().toISOString()} [${level}] ${message}${metadata}\n`;

    // stdout belongs exclusively to MCP's stdio transport.
    process.stderr.write(line);
    try {
      appendFileSync(this.logFilePath, line, "utf8");
    } catch (error) {
      process.stderr.write(`${new Date().toISOString()} [ERROR] Failed to write log file: ${String(error)}\n`);
    }
  }
}
