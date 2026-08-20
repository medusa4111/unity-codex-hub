import { lstat, readFile, realpath, stat, unlink } from "node:fs/promises";
import path from "node:path";
import { HubError } from "../protocol/error.js";

const CAPTURE_DIRECTORY = "Library/UnityCodexBridge/Captures";
const MAX_CAPTURE_BYTES = 20 * 1024 * 1024;
const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

export interface CaptureMetadata extends Record<string, unknown> {
  capturePath: string;
  mimeType: "image/png";
}

export interface ReadCapture {
  metadata: CaptureMetadata;
  data: string;
  mimeType: "image/png";
}

export async function readGeneratedCapture(
  rawMetadata: unknown,
  projectRoot: string | null,
  deleteAfterRead = true,
): Promise<ReadCapture> {
  if (projectRoot === null || projectRoot.trim() === "") {
    throw new HubError("UNITY_NOT_CONNECTED", "Unity project path is unavailable for capture validation");
  }
  if (!isRecord(rawMetadata)) {
    throw new HubError("INVALID_RESPONSE", "Unity returned invalid capture metadata");
  }

  const capturePath = rawMetadata["capturePath"];
  const mimeType = rawMetadata["mimeType"];
  if (typeof capturePath !== "string" || mimeType !== "image/png") {
    throw new HubError("INVALID_RESPONSE", "Unity capture metadata must contain a PNG capturePath");
  }
  validateRelativeCapturePath(capturePath);

  const canonicalProject = await realpath(projectRoot).catch(() => {
    throw new HubError("INVALID_RESPONSE", "Unity project root does not exist");
  });
  const canonicalCaptureDirectory = await realpath(path.join(canonicalProject, CAPTURE_DIRECTORY)).catch(() => {
    throw new HubError("INVALID_RESPONSE", "Unity capture directory does not exist");
  });
  ensureContained(canonicalProject, canonicalCaptureDirectory, "Capture directory escapes the Unity project");

  const candidate = path.resolve(canonicalProject, capturePath);
  ensureContained(canonicalCaptureDirectory, candidate, "Capture path escapes the controlled capture directory");
  const linkInfo = await lstat(candidate).catch(() => {
    throw new HubError("INVALID_RESPONSE", "Unity capture file does not exist");
  });
  if (linkInfo.isSymbolicLink() || !linkInfo.isFile()) {
    throw new HubError("INVALID_RESPONSE", "Unity capture path must identify a regular non-symlink file");
  }
  const canonicalFile = await realpath(candidate);
  ensureContained(canonicalCaptureDirectory, canonicalFile, "Capture file resolves outside the controlled directory");
  const fileInfo = await stat(canonicalFile);
  if (fileInfo.size < PNG_SIGNATURE.length) {
    throw new HubError("INVALID_RESPONSE", "Unity capture file is too small to be a PNG");
  }
  if (fileInfo.size > MAX_CAPTURE_BYTES) {
    throw new HubError("RESULT_TOO_LARGE", `Capture exceeds the ${MAX_CAPTURE_BYTES}-byte safety limit`);
  }
  const bytes = await readFile(canonicalFile);
  if (!bytes.subarray(0, PNG_SIGNATURE.length).equals(PNG_SIGNATURE)) {
    throw new HubError("INVALID_RESPONSE", "Unity capture file does not have a valid PNG signature");
  }
  if (deleteAfterRead) {
    await unlink(canonicalFile).catch(() => undefined);
  }

  return {
    metadata: { ...rawMetadata, capturePath, mimeType },
    data: bytes.toString("base64"),
    mimeType,
  };
}

function validateRelativeCapturePath(value: string): void {
  if (value.includes("\\") || path.isAbsolute(value) || value.includes("\0")) {
    throw new HubError("INVALID_RESPONSE", "Capture path must be a normalized project-relative path");
  }
  const segments = value.split("/");
  if (segments.some((segment) => segment === "" || segment === "." || segment === "..")) {
    throw new HubError("INVALID_RESPONSE", "Capture path contains an invalid segment");
  }
  if (!value.startsWith(`${CAPTURE_DIRECTORY}/`) || !value.toLowerCase().endsWith(".png")) {
    throw new HubError("INVALID_RESPONSE", "Capture path is outside the controlled PNG capture directory");
  }
}

function ensureContained(root: string, candidate: string, message: string): void {
  const relative = path.relative(root, candidate);
  if (relative === "" || (!relative.startsWith("..") && !path.isAbsolute(relative))) {
    return;
  }
  throw new HubError("INVALID_RESPONSE", message);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
