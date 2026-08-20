import assert from "node:assert/strict";
import test from "node:test";
import {
  assetReferenceSchema,
  batchSchema,
  batchSetTransformsSchema,
  closeSceneSchema,
  scatterPrefabSchema,
  setSelectionSchema,
  setTerrainAlphamapPatchSchema,
} from "../src/protocol/schemas.js";

test("asset and selection references are strict, exclusive, and project-relative", () => {
  assert.equal(assetReferenceSchema.safeParse({ assetPath: "Assets/Props/Crate.prefab" }).success, true);
  assert.equal(assetReferenceSchema.safeParse({ guid: "0123456789abcdef0123456789abcdef" }).success, true);
  assert.equal(assetReferenceSchema.safeParse({}).success, false);
  assert.equal(assetReferenceSchema.safeParse({ assetPath: "Assets/a.mat", guid: "0123456789abcdef0123456789abcdef" }).success, false);
  assert.equal(assetReferenceSchema.safeParse({ assetPath: "Assets/../secret" }).success, false);
  assert.equal(assetReferenceSchema.safeParse({ assetPath: "/tmp/file.mat" }).success, false);
  assert.equal(setSelectionSchema.safeParse({ objects: [{ hierarchyPath: "Root" }, { assetPath: "Assets/a.mat" }] }).success, true);
  assert.equal(setSelectionSchema.safeParse({ objects: [{ hierarchyPath: "Root", assetPath: "Assets/a.mat" }] }).success, false);
});

test("batch accepts only validated allowlisted operations", () => {
  assert.equal(batchSchema.safeParse({ operations: [{ command: "set_transform", instanceId: "1", position: { x: 1, y: 2, z: 3 } }] }).success, true);
  assert.equal(batchSchema.safeParse({ operations: [{ command: "set_transform", instanceId: "1" }] }).success, false);
  assert.equal(batchSchema.safeParse({ operations: [{ command: "set_transform", instanceId: "1", hierarchyPath: "Root", position: { x: 0, y: 0, z: 0 } }] }).success, false);
  assert.equal(batchSchema.safeParse({ operations: [{ command: "save_scene" }] }).success, false);
  assert.equal(batchSetTransformsSchema.safeParse({ items: [{ hierarchyPath: "Root", space: "world", scale: { x: 1, y: 1, z: 1 } }] }).success, false);
});

test("scatter, dirty Scene policies, and Terrain matrices enforce bounded invariants", () => {
  const scatterBase = {
    assetPath: "Assets/Tree.prefab",
    count: 10,
    seed: 42,
    center: { x: 0, y: 0, z: 0 },
  };
  assert.equal(scatterPrefabSchema.safeParse({ ...scatterBase, radius: 5 }).success, true);
  assert.equal(scatterPrefabSchema.safeParse({ ...scatterBase, radius: 5, size: { x: 1, y: 1, z: 1 } }).success, false);
  assert.equal(scatterPrefabSchema.safeParse({ ...scatterBase }).success, false);
  assert.equal(scatterPrefabSchema.safeParse({ ...scatterBase, radius: 5, minScale: 2, maxScale: 1 }).success, false);
  assert.equal(closeSceneSchema.safeParse({ scenePath: "Assets/Test.unity", saveModified: true, discardModified: true }).success, false);
  assert.equal(setTerrainAlphamapPatchSchema.safeParse({
    hierarchyPath: "Terrain",
    x: 0,
    y: 0,
    values: [[[0.25, 0.75]]],
  }).success, true);
  assert.equal(setTerrainAlphamapPatchSchema.safeParse({
    hierarchyPath: "Terrain",
    x: 0,
    y: 0,
    values: [[[1.1]]],
  }).success, false);
});
