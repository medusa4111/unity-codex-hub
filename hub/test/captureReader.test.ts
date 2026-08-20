import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { readGeneratedCapture } from "../src/capture/captureReader.js";

const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3]);

test("capture reader accepts only generated PNG files and deletes them after reading", async () => {
  const project = mkdtempSync(path.join(tmpdir(), "unity-capture-project-"));
  const captureDirectory = path.join(project, "Library/UnityCodexBridge/Captures");
  mkdirSync(captureDirectory, { recursive: true });
  const capture = path.join(captureDirectory, "camera-1.png");
  writeFileSync(capture, png);
  try {
    const result = await readGeneratedCapture({
      capturePath: "Library/UnityCodexBridge/Captures/camera-1.png",
      mimeType: "image/png",
      width: 64,
      height: 64,
    }, project);
    assert.equal(Buffer.from(result.data, "base64").equals(png), true);
    assert.equal(result.metadata.width, 64);
    assert.equal(existsSync(capture), false);
  } finally {
    rmSync(project, { recursive: true, force: true });
  }
});

test("capture reader rejects traversal, absolute paths, non-PNG data, and symlinks", async () => {
  const project = mkdtempSync(path.join(tmpdir(), "unity-capture-security-"));
  const captureDirectory = path.join(project, "Library/UnityCodexBridge/Captures");
  mkdirSync(captureDirectory, { recursive: true });
  writeFileSync(path.join(captureDirectory, "bad.png"), Buffer.from("not png"));
  const outside = path.join(project, "outside.png");
  writeFileSync(outside, png);
  symlinkSync(outside, path.join(captureDirectory, "link.png"));
  const metadata = (capturePath: string) => ({ capturePath, mimeType: "image/png" });
  try {
    await assert.rejects(readGeneratedCapture(metadata("Library/UnityCodexBridge/Captures/../outside.png"), project), hasInvalidResponse);
    await assert.rejects(readGeneratedCapture(metadata(outside), project), hasInvalidResponse);
    await assert.rejects(readGeneratedCapture(metadata("Library/UnityCodexBridge/Captures/bad.png"), project), hasInvalidResponse);
    await assert.rejects(readGeneratedCapture(metadata("Library/UnityCodexBridge/Captures/link.png"), project), hasInvalidResponse);
  } finally {
    rmSync(project, { recursive: true, force: true });
  }
});

function hasInvalidResponse(error: unknown): boolean {
  return error instanceof Error && "code" in error && error.code === "INVALID_RESPONSE";
}
