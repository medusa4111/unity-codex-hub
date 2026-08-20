import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { HubError, errorMessage, type ProtocolError } from "../protocol/error.js";

interface ToolSuccessEnvelope extends Record<string, unknown> {
  success: true;
  result: unknown;
  error: null;
}

interface ToolFailureEnvelope extends Record<string, unknown> {
  success: false;
  result: null;
  error: ProtocolError;
}

function content(envelope: ToolSuccessEnvelope | ToolFailureEnvelope): CallToolResult["content"] {
  return [{ type: "text", text: JSON.stringify(envelope, null, 2) }];
}

export function toolSuccess(result: unknown): CallToolResult {
  const envelope: ToolSuccessEnvelope = { success: true, result, error: null };
  return {
    content: content(envelope),
    structuredContent: envelope,
  };
}

export function toolImageSuccess(result: unknown, data: string, mimeType: "image/png"): CallToolResult {
  const envelope: ToolSuccessEnvelope = { success: true, result, error: null };
  return {
    content: [
      ...content(envelope),
      { type: "image", data, mimeType },
    ],
    structuredContent: envelope,
  };
}

export function toolFailure(error: unknown): CallToolResult {
  const protocolError = error instanceof HubError
    ? error.toProtocolError()
    : { code: "INTERNAL_ERROR" as const, message: errorMessage(error) };
  const envelope: ToolFailureEnvelope = { success: false, result: null, error: protocolError };
  return {
    content: content(envelope),
    structuredContent: envelope,
    isError: true,
  };
}
