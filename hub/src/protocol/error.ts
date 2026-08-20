export const ERROR_CODES = [
  "UNITY_NOT_CONNECTED",
  "OBJECT_NOT_FOUND",
  "COMPONENT_NOT_FOUND",
  "PROPERTY_NOT_FOUND",
  "ASSET_NOT_FOUND",
  "SCENE_NOT_FOUND",
  "PREFAB_NOT_FOUND",
  "TYPE_NOT_FOUND",
  "CAPABILITY_UNAVAILABLE",
  "INVALID_ASSET_PATH",
  "INVALID_SCENE_STATE",
  "PLAY_MODE_TRANSITION",
  "RESULT_TOO_LARGE",
  "JOB_NOT_FOUND",
  "INVALID_RESPONSE",
  "INVALID_ARGUMENT",
  "UNITY_BUSY",
  "UNITY_COMPILING",
  "TIMEOUT",
  "COMMAND_FAILED",
  "INTERNAL_ERROR",
] as const;

export type ErrorCode = (typeof ERROR_CODES)[number];

export interface ProtocolError {
  code: ErrorCode;
  message: string;
  details?: unknown;
}

export class HubError extends Error {
  readonly code: ErrorCode;
  readonly details?: unknown;

  constructor(code: ErrorCode, message: string, details?: unknown) {
    super(message);
    this.name = "HubError";
    this.code = code;
    this.details = details;
  }

  toProtocolError(): ProtocolError {
    return this.details === undefined
      ? { code: this.code, message: this.message }
      : { code: this.code, message: this.message, details: this.details };
  }
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
