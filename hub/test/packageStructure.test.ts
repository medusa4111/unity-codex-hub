import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { UNITY_COMMANDS } from "../src/protocol/messages.js";
import {
  createGameObjectSchema,
  objectReferenceSchema,
  setComponentPropertySchema,
} from "../src/protocol/schemas.js";

function collectFiles(directory: string): string[] {
  const result: string[] = [];
  for (const name of readdirSync(directory)) {
    const filePath = path.join(directory, name);
    if (statSync(filePath).isDirectory()) result.push(...collectFiles(filePath));
    else result.push(filePath);
  }
  return result;
}

test("Unity package is editor-only and keeps its command allowlist in protocol parity", () => {
  const testDirectory = path.dirname(fileURLToPath(import.meta.url));
  const repositoryRoot = path.resolve(testDirectory, "../../..");
  const packageRoot = path.join(
    repositoryRoot,
    "unity-package/Packages/com.codex.unitybridge",
  );
  const manifest = JSON.parse(readFileSync(path.join(packageRoot, "package.json"), "utf8")) as {
    name: string;
    version: string;
    unity: string;
  };
  assert.equal(manifest.name, "com.codex.unitybridge");
  assert.equal(manifest.version, "0.2.0");
  assert.equal(manifest.unity, "2021.3");

  const assemblyDefinition = JSON.parse(readFileSync(
    path.join(packageRoot, "Editor/Codex.UnityBridge.Editor.asmdef"),
    "utf8",
  )) as { includePlatforms: string[] };
  assert.deepEqual(assemblyDefinition.includePlatforms, ["Editor"]);

  const csharpFiles = collectFiles(path.join(packageRoot, "Editor"))
    .filter((filePath) => filePath.endsWith(".cs"));
  assert(csharpFiles.length >= 20);
  for (const csharpFile of csharpFiles) {
    assert.equal(statSync(`${csharpFile}.meta`).isFile(), true, `${csharpFile} must have a Unity meta file`);
  }
  const csharpSource = csharpFiles.map((filePath) => readFileSync(filePath, "utf8")).join("\n");
  assert.match(csharpSource, /ConcurrentQueue<UnityCommandRequest>/);
  assert.match(csharpSource, /EditorApplication\.update \+= OnEditorUpdate/);
  assert.match(csharpSource, /UNITY_6000_5_OR_NEWER/);
  assert.match(csharpSource, /EntityId\.ToULong/);
  assert.match(csharpSource, /EditorUtility\.EntityIdToObject/);
  assert.doesNotMatch(csharpSource, /Process\.Start|System\.Diagnostics\.Process|CSharpCodeProvider/);
  assert.doesNotMatch(csharpSource, /OpenFilePanel|SaveFilePanel|DisplayDialog|Application\.OpenURL/);
  assert.match(csharpSource, /Library\/UnityCodexBridge\/Captures/);
  assert.match(csharpSource, /Undo\./);

  const parserSource = readFileSync(path.join(packageRoot, "Editor/Protocol/ProtocolParser.cs"), "utf8");
  for (const command of UNITY_COMMANDS) {
    assert.match(parserSource, new RegExp(`"${command}"`));
  }
});

test("object references accept Unity 6000.5 EntityId strings and legacy integer IDs", () => {
  assert.deepEqual(
    objectReferenceSchema.parse({ instanceId: "4294967297" }),
    { instanceId: "4294967297" },
  );
  assert.deepEqual(
    objectReferenceSchema.parse({ instanceId: 12345 }),
    { instanceId: 12345 },
  );
  assert.deepEqual(
    createGameObjectSchema.parse({ name: "Child", parentInstanceId: "4294967297" }),
    { name: "Child", parentInstanceId: "4294967297" },
  );
  assert.deepEqual(
    setComponentPropertySchema.parse({
      hierarchyPath: "Player",
      componentInstanceId: "4294967298",
      propertyPath: "enabled",
      value: true,
    }),
    {
      hierarchyPath: "Player",
      componentInstanceId: "4294967298",
      propertyPath: "enabled",
      value: true,
    },
  );
  assert.equal(objectReferenceSchema.safeParse({ instanceId: "" }).success, false);
});
