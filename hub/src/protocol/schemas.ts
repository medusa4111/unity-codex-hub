import { z } from "zod";

export const emptyParamsSchema = z.object({}).strict();

export const unityObjectIdSchema = z.union([
  z.string().trim().regex(/^-?\d+$/, "Unity object ID must be an integer string"),
  z.number().int(),
]);

const assetPathSchema = z.string().trim().min(8).max(1024)
  .refine((value) => value.startsWith("Assets/"), "Path must be under Assets/")
  .refine((value) => !value.includes("\\") && !value.split("/").includes(".."), "Path must be normalized");

const guidSchema = z.string().trim().regex(/^[0-9a-fA-F]{32}$/, "GUID must contain 32 hexadecimal characters");

export const assetReferenceShape = {
  assetPath: assetPathSchema.optional(),
  guid: guidSchema.optional(),
};

function validateAssetReference(
  value: { assetPath?: string | undefined; guid?: string | undefined },
  context: z.RefinementCtx,
): void {
  if ((value.assetPath === undefined) === (value.guid === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of assetPath or guid" });
  }
}

export const assetReferenceSchema = z.object(assetReferenceShape).strict().superRefine(validateAssetReference);

export const objectReferenceShape = {
  instanceId: unityObjectIdSchema.optional(),
  hierarchyPath: z.string().trim().min(1).max(2048).optional(),
};

function validateObjectReference(
  value: { instanceId?: string | number | undefined; hierarchyPath?: string | undefined },
  context: z.RefinementCtx,
): void {
  if ((value.instanceId === undefined) === (value.hierarchyPath === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of instanceId or hierarchyPath" });
  }
}

export const objectReferenceSchema = z.object(objectReferenceShape).strict().superRefine(validateObjectReference);

const componentReferenceShape = {
  componentInstanceId: unityObjectIdSchema.optional(),
  componentType: z.string().trim().min(1).max(512).optional(),
};

function validateComponentReference(
  value: { componentInstanceId?: string | number | undefined; componentType?: string | undefined },
  context: z.RefinementCtx,
): void {
  if ((value.componentInstanceId === undefined) === (value.componentType === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of componentInstanceId or componentType" });
  }
}

export const vector2Schema = z.object({ x: z.number().finite(), y: z.number().finite() }).strict();
export const vector3Schema = z.object({
  x: z.number().finite(), y: z.number().finite(), z: z.number().finite(),
}).strict();
export const vector4Schema = z.object({
  x: z.number().finite(), y: z.number().finite(), z: z.number().finite(), w: z.number().finite(),
}).strict();
export const colorSchema = z.object({
  r: z.number().finite(), g: z.number().finite(), b: z.number().finite(), a: z.number().finite().optional(),
}).strict();
export const quaternionSchema = z.object({
  x: z.number().finite(), y: z.number().finite(), z: z.number().finite(), w: z.number().finite(),
}).strict();

type JsonValue = null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue };
export const serializedValueSchema: z.ZodType<JsonValue> = z.lazy(() => z.union([
  z.null(),
  z.boolean(),
  z.number().finite(),
  z.string().max(16_384),
  z.array(serializedValueSchema).max(4096),
  z.record(z.string().max(512), serializedValueSchema),
]));

const parentReferenceShape = {
  parentInstanceId: unityObjectIdSchema.optional(),
  parentPath: z.string().trim().min(1).max(2048).optional(),
};

function validateOptionalParent(
  value: { parentInstanceId?: string | number | undefined; parentPath?: string | undefined },
  context: z.RefinementCtx,
): void {
  if (value.parentInstanceId !== undefined && value.parentPath !== undefined) {
    context.addIssue({ code: "custom", message: "Provide only one of parentInstanceId or parentPath" });
  }
}

const transformValuesShape = {
  position: vector3Schema.optional(),
  rotation: vector3Schema.optional(),
  scale: vector3Schema.optional(),
};

export const refreshAssetsSchema = z.object({
  forceUpdate: z.boolean().default(false),
}).strict();

export const waitSchema = z.object({
  timeoutMs: z.number().int().min(1_000).max(300_000).default(120_000),
  pollIntervalMs: z.number().int().min(50).max(2_000).default(250),
}).strict();

export const hierarchySchema = z.object({
  maxDepth: z.number().int().min(0).max(64).default(16),
  maxItems: z.number().int().min(1).max(1_000).default(500),
}).strict();

export const findGameObjectsSchema = z.object({
  name: z.string().trim().min(1).max(256).optional(),
  partialName: z.string().trim().min(1).max(256).optional(),
  componentType: z.string().trim().min(1).max(512).optional(),
  tag: z.string().trim().min(1).max(256).optional(),
  layer: z.union([z.number().int().min(0).max(31), z.string().trim().min(1).max(256)]).optional(),
  active: z.boolean().optional(),
  includeInactive: z.boolean().default(true),
  scenePath: assetPathSchema.optional(),
  offset: z.number().int().min(0).max(1_000_000).default(0),
  limit: z.number().int().min(1).max(500).default(100),
}).strict().refine((value) => value.name === undefined || value.partialName === undefined, {
  message: "Provide only one of name or partialName",
});

export const componentPropertiesSchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
  maxDepth: z.number().int().min(0).max(16).default(4),
  maxItems: z.number().int().min(1).max(1000).default(200),
  includeHidden: z.boolean().default(false),
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

export const createGameObjectSchema = z.object({
  name: z.string().trim().min(1).max(256),
  ...parentReferenceShape,
}).strict().superRefine(validateOptionalParent);

export const createPrimitiveSchema = z.object({
  primitiveType: z.enum(["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"]),
  name: z.string().trim().min(1).max(256).optional(),
  ...parentReferenceShape,
  ...transformValuesShape,
}).strict().superRefine(validateOptionalParent);

export const duplicateGameObjectSchema = z.object({
  ...objectReferenceShape,
  newName: z.string().trim().min(1).max(256).optional(),
  ...parentReferenceShape,
  worldPositionStays: z.boolean().default(true),
  ...transformValuesShape,
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateOptionalParent(value, context);
});

export const reparentGameObjectSchema = z.object({
  ...objectReferenceShape,
  ...parentReferenceShape,
  worldPositionStays: z.boolean().default(true),
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateOptionalParent(value, context);
});

export const setGameObjectPropertiesSchema = z.object({
  ...objectReferenceShape,
  name: z.string().trim().min(1).max(256).optional(),
  active: z.boolean().optional(),
  tag: z.string().trim().min(1).max(256).optional(),
  layer: z.union([z.number().int().min(0).max(31), z.string().trim().min(1).max(256)]).optional(),
  staticFlags: z.number().int().min(0).optional(),
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  if (value.name === undefined && value.active === undefined && value.tag === undefined
    && value.layer === undefined && value.staticFlags === undefined) {
    context.addIssue({ code: "custom", message: "Provide at least one GameObject property" });
  }
});

export const setTransformSchema = z.object({
  ...objectReferenceShape,
  space: z.enum(["local", "world"]).default("local"),
  ...transformValuesShape,
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  if (value.position === undefined && value.rotation === undefined && value.scale === undefined) {
    context.addIssue({ code: "custom", message: "Provide position, rotation, or scale" });
  }
  if (value.space === "world" && value.scale !== undefined) {
    context.addIssue({ code: "custom", message: "World-space scale is not supported" });
  }
});

export const addComponentSchema = z.object({
  ...objectReferenceShape,
  componentType: z.string().trim().min(1).max(512),
}).strict().superRefine(validateObjectReference);

export const removeComponentSchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

const propertyChangeSchema = z.object({
  propertyPath: z.string().trim().min(1).max(1024),
  value: serializedValueSchema,
}).strict();

export const setComponentPropertySchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
  propertyPath: z.string().trim().min(1).max(1024),
  value: serializedValueSchema,
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

export const setComponentPropertiesSchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
  properties: z.array(propertyChangeSchema).min(1).max(128),
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

export const resizeSerializedArraySchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
  propertyPath: z.string().trim().min(1).max(1024),
  size: z.number().int().min(0).max(4096),
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

export const setSerializedArrayElementSchema = z.object({
  ...objectReferenceShape,
  ...componentReferenceShape,
  propertyPath: z.string().trim().min(1).max(1024),
  index: z.number().int().min(0).max(4095),
  value: serializedValueSchema,
}).strict().superRefine((value, context) => {
  validateObjectReference(value, context);
  validateComponentReference(value, context);
});

export const findAssetsSchema = z.object({
  query: z.string().max(1024).default(""),
  type: z.string().trim().min(1).max(256).optional(),
  folders: z.array(z.string().trim().min(6).max(1024)
    .refine((value) => value === "Assets" || value.startsWith("Assets/"), "Folder must be under Assets")
    .refine((value) => !value.includes("\\") && !value.split("/").includes(".."), "Folder must be normalized"))
    .max(32).optional(),
  offset: z.number().int().min(0).max(1_000_000).default(0),
  limit: z.number().int().min(1).max(500).default(100),
}).strict();

export const assetInfoSchema = z.object({
  ...assetReferenceShape,
  includeDependencies: z.boolean().default(false),
  dependencyLimit: z.number().int().min(1).max(1000).default(200),
}).strict().superRefine(validateAssetReference);

export const assetDependenciesSchema = z.object({
  ...assetReferenceShape,
  recursive: z.boolean().default(true),
  offset: z.number().int().min(0).max(1_000_000).default(0),
  limit: z.number().int().min(1).max(2000).default(500),
}).strict().superRefine(validateAssetReference);

export const importAssetSchema = z.object({
  assetPath: assetPathSchema,
  forceUpdate: z.boolean().default(false),
  forceSynchronousImport: z.boolean().default(false),
}).strict();

export const assetPreviewSchema = z.object({
  ...assetReferenceShape,
  width: z.number().int().min(32).max(1024).default(256),
  height: z.number().int().min(32).max(1024).default(256),
}).strict().superRefine(validateAssetReference);

const prefabTransformShape = {
  ...parentReferenceShape,
  ...transformValuesShape,
};

export const instantiatePrefabSchema = z.object({
  ...assetReferenceShape,
  ...prefabTransformShape,
  name: z.string().trim().min(1).max(256).optional(),
}).strict().superRefine((value, context) => {
  validateAssetReference(value, context);
  validateOptionalParent(value, context);
});

export const prefabInfoSchema = z.object({
  ...objectReferenceShape,
}).strict().superRefine(validateObjectReference);

export const savePrefabSchema = z.object({
  ...objectReferenceShape,
  assetPath: assetPathSchema.refine((value) => value.endsWith(".prefab"), "Prefab path must end in .prefab"),
  overwrite: z.boolean().default(false),
}).strict().superRefine(validateObjectReference);

export const prefabMutationSchema = z.object({
  ...objectReferenceShape,
}).strict().superRefine(validateObjectReference);

export const createMaterialSchema = z.object({
  assetPath: assetPathSchema.refine((value) => value.endsWith(".mat"), "Material path must end in .mat"),
  shaderName: z.string().trim().min(1).max(512),
  overwrite: z.boolean().default(false),
}).strict();

export const materialPropertiesSchema = z.object(assetReferenceShape).strict().superRefine(validateAssetReference);

export const setMaterialPropertySchema = z.object({
  ...assetReferenceShape,
  propertyName: z.string().trim().min(1).max(512),
  value: serializedValueSchema,
}).strict().superRefine(validateAssetReference);

export const createScriptableObjectSchema = z.object({
  type: z.string().trim().min(1).max(512),
  assetPath: assetPathSchema.refine((value) => value.endsWith(".asset"), "ScriptableObject path must end in .asset"),
  initialProperties: z.array(propertyChangeSchema).max(128).default([]),
  overwrite: z.boolean().default(false),
}).strict();

export const listScenesSchema = z.object({
  includePackages: z.boolean().default(false),
  offset: z.number().int().min(0).max(1_000_000).default(0),
  limit: z.number().int().min(1).max(1000).default(200),
}).strict();

const dirtyScenePolicyShape = {
  saveModified: z.boolean().default(false),
  discardModified: z.boolean().default(false),
};

function validateDirtyPolicy(
  value: { saveModified?: boolean | undefined; discardModified?: boolean | undefined },
  context: z.RefinementCtx,
): void {
  if (value.saveModified && value.discardModified) {
    context.addIssue({ code: "custom", message: "saveModified and discardModified are mutually exclusive" });
  }
}

export const newSceneSchema = z.object({
  setup: z.enum(["EmptyScene", "DefaultGameObjects"]).default("EmptyScene"),
  mode: z.enum(["Single", "Additive"]).default("Single"),
  ...dirtyScenePolicyShape,
}).strict().superRefine(validateDirtyPolicy);

export const openSceneSchema = z.object({
  scenePath: assetPathSchema.refine((value) => value.endsWith(".unity"), "Scene path must end in .unity"),
  mode: z.enum(["Single", "Additive"]).default("Single"),
  ...dirtyScenePolicyShape,
}).strict().superRefine(validateDirtyPolicy);

export const sceneReferenceSchema = z.object({
  scenePath: assetPathSchema.optional(),
  sceneName: z.string().trim().min(1).max(256).optional(),
}).strict().superRefine((value, context) => {
  if ((value.scenePath === undefined) === (value.sceneName === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of scenePath or sceneName" });
  }
});

export const saveSceneSchema = z.object({
  scenePath: assetPathSchema.optional(),
  sceneName: z.string().trim().min(1).max(256).optional(),
}).strict().refine((value) => value.scenePath === undefined || value.sceneName === undefined, {
  message: "Provide only one of scenePath or sceneName",
});

export const saveSceneAsSchema = z.object({
  scenePath: assetPathSchema.optional(),
  sceneName: z.string().trim().min(1).max(256).optional(),
  destinationPath: assetPathSchema.refine((value) => value.endsWith(".unity"), "Scene path must end in .unity"),
  overwrite: z.boolean().default(false),
}).strict().refine((value) => value.scenePath === undefined || value.sceneName === undefined, {
  message: "Provide only one of scenePath or sceneName",
});

export const closeSceneSchema = z.object({
  scenePath: assetPathSchema.optional(),
  sceneName: z.string().trim().min(1).max(256).optional(),
  saveModified: z.boolean().default(false),
  discardModified: z.boolean().default(false),
  removeScene: z.boolean().default(true),
}).strict().superRefine((value, context) => {
  if ((value.scenePath === undefined) === (value.sceneName === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of scenePath or sceneName" });
  }
  validateDirtyPolicy(value, context);
});

export const pausePlayModeSchema = z.object({ paused: z.boolean().default(true) }).strict();
export const waitForPlayModeSchema = z.object({
  state: z.enum(["playing", "stopped", "paused"]),
  timeoutMs: z.number().int().min(1_000).max(300_000).default(120_000),
  pollIntervalMs: z.number().int().min(50).max(2_000).default(250),
}).strict();

const captureShape = {
  width: z.number().int().min(64).max(4096).default(1280),
  height: z.number().int().min(64).max(4096).default(720),
  transparentBackground: z.boolean().default(false),
};

export const captureGameViewSchema = z.object(captureShape).strict();
export const captureCameraSchema = z.object({ ...objectReferenceShape, ...captureShape })
  .strict().superRefine(validateObjectReference);
export const captureSceneViewSchema = z.object(captureShape).strict();

export const getConsoleSchema = z.object({
  severities: z.array(z.enum(["Error", "Warning", "Log"])).min(1).max(3)
    .default(["Error", "Warning", "Log"]),
  search: z.string().max(1024).optional(),
  sinceSequence: z.number().int().min(0).default(0),
  maxResults: z.number().int().min(1).max(1000).default(200),
  includeStackTrace: z.boolean().default(true),
  errorsOnly: z.boolean().optional(),
}).strict();

const batchOperationSchema = z.discriminatedUnion("command", [
  z.object({ command: z.literal("create_game_object"), name: z.string().trim().min(1).max(256), ...parentReferenceShape }).strict(),
  z.object({ command: z.literal("create_primitive"), primitiveType: z.enum(["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"]), name: z.string().trim().min(1).max(256).optional(), ...parentReferenceShape, ...transformValuesShape }).strict(),
  z.object({ command: z.literal("set_transform"), ...objectReferenceShape, space: z.enum(["local", "world"]).default("local"), ...transformValuesShape }).strict(),
  z.object({ command: z.literal("set_game_object_properties"), ...objectReferenceShape, name: z.string().trim().min(1).max(256).optional(), active: z.boolean().optional(), tag: z.string().trim().min(1).max(256).optional(), layer: z.union([z.number().int().min(0).max(31), z.string().trim().min(1)]).optional() }).strict(),
  z.object({ command: z.literal("delete_game_object"), ...objectReferenceShape }).strict(),
]);

export const batchSchema = z.object({
  operations: z.array(batchOperationSchema).min(1).max(100),
  stopOnError: z.boolean().default(true),
  undoGroupName: z.string().trim().min(1).max(256).default("Codex: Batch"),
}).strict().superRefine((value, context) => {
  value.operations.forEach((operation, index) => {
    const issue = (message: string): void => context.addIssue({
      code: "custom", message, path: ["operations", index],
    });
    if (operation.command === "create_game_object" || operation.command === "create_primitive") {
      if (operation.parentInstanceId !== undefined && operation.parentPath !== undefined) {
        issue("Provide only one parent reference");
      }
    }
    if (operation.command === "set_transform") {
      if ((operation.instanceId === undefined) === (operation.hierarchyPath === undefined)) {
        issue("Provide exactly one object reference");
      }
      if (operation.position === undefined && operation.rotation === undefined && operation.scale === undefined) {
        issue("Provide position, rotation, or scale");
      }
      if (operation.space === "world" && operation.scale !== undefined) {
        issue("World-space scale is not supported");
      }
    }
    if (operation.command === "set_game_object_properties") {
      if ((operation.instanceId === undefined) === (operation.hierarchyPath === undefined)) {
        issue("Provide exactly one object reference");
      }
      if (operation.name === undefined && operation.active === undefined && operation.tag === undefined
        && operation.layer === undefined) {
        issue("Provide at least one GameObject property");
      }
    }
    if (operation.command === "delete_game_object"
      && (operation.instanceId === undefined) === (operation.hierarchyPath === undefined)) {
      issue("Provide exactly one object reference");
    }
  });
});

const prefabPlacementSchema = z.object({
  ...transformValuesShape,
  name: z.string().trim().min(1).max(256).optional(),
}).strict();

export const batchInstantiatePrefabSchema = z.object({
  ...assetReferenceShape,
  ...parentReferenceShape,
  placements: z.array(prefabPlacementSchema).min(1).max(500),
  undoGroupName: z.string().trim().min(1).max(256).default("Codex: Batch Instantiate Prefab"),
}).strict().superRefine((value, context) => {
  validateAssetReference(value, context);
  validateOptionalParent(value, context);
});

export const batchSetTransformsSchema = z.object({
  items: z.array(z.object({
    ...objectReferenceShape,
    space: z.enum(["local", "world"]).default("local"),
    ...transformValuesShape,
  }).strict()).min(1).max(1000),
  stopOnError: z.boolean().default(true),
  undoGroupName: z.string().trim().min(1).max(256).default("Codex: Batch Transform"),
}).strict().superRefine((value, context) => {
  value.items.forEach((item, index) => {
    const issue = (message: string): void => context.addIssue({ code: "custom", message, path: ["items", index] });
    if ((item.instanceId === undefined) === (item.hierarchyPath === undefined)) {
      issue("Provide exactly one object reference");
    }
    if (item.position === undefined && item.rotation === undefined && item.scale === undefined) {
      issue("Provide position, rotation, or scale");
    }
    if (item.space === "world" && item.scale !== undefined) issue("World-space scale is not supported");
  });
});

export const scatterPrefabSchema = z.object({
  ...assetReferenceShape,
  ...parentReferenceShape,
  count: z.number().int().min(1).max(2000),
  seed: z.number().int(),
  center: vector3Schema,
  size: vector3Schema.optional(),
  radius: z.number().positive().max(100_000).optional(),
  minScale: z.number().positive().max(1000).default(1),
  maxScale: z.number().positive().max(1000).default(1),
  randomYaw: z.boolean().default(true),
  alignToSurface: z.boolean().default(false),
  raycastDirection: vector3Schema.default({ x: 0, y: -1, z: 0 }),
  layerMask: z.number().int().default(-1),
  minSpacing: z.number().min(0).max(100_000).default(0),
  maxReturnedObjects: z.number().int().min(0).max(500).default(100),
}).strict().superRefine((value, context) => {
  validateAssetReference(value, context);
  validateOptionalParent(value, context);
  if ((value.size === undefined) === (value.radius === undefined)) {
    context.addIssue({ code: "custom", message: "Provide exactly one of size or radius" });
  }
  if (value.maxScale < value.minScale) {
    context.addIssue({ code: "custom", message: "maxScale must be >= minScale" });
  }
});

export const createTerrainSchema = z.object({
  assetPath: assetPathSchema.refine((value) => value.endsWith(".asset"), "TerrainData path must end in .asset"),
  name: z.string().trim().min(1).max(256).optional(),
  position: vector3Schema.default({ x: 0, y: 0, z: 0 }),
  size: vector3Schema,
  heightmapResolution: z.number().int().min(33).max(4097).default(513),
  alphamapResolution: z.number().int().min(16).max(2048).default(512),
  overwrite: z.boolean().default(false),
}).strict();

export const terrainReferenceSchema = z.object({ ...objectReferenceShape }).strict().superRefine(validateObjectReference);
const heightsSchema = z.array(z.array(z.number().min(0).max(1)).min(1).max(513)).min(1).max(513);
export const setTerrainHeightsSchema = z.object({ ...objectReferenceShape, heights: heightsSchema }).strict().superRefine(validateObjectReference);
export const setTerrainHeightsPatchSchema = z.object({
  ...objectReferenceShape,
  xBase: z.number().int().min(0), yBase: z.number().int().min(0), heights: heightsSchema,
}).strict().superRefine(validateObjectReference);

export const setTerrainLayersSchema = z.object({
  ...objectReferenceShape,
  layers: z.array(assetReferenceSchema).min(1).max(32),
}).strict().superRefine(validateObjectReference);

const alphaMapSchema = z.array(z.array(z.array(z.number().min(0).max(1)).min(1).max(32)).min(1).max(128)).min(1).max(128);
export const setTerrainAlphamapPatchSchema = z.object({
  ...objectReferenceShape,
  x: z.number().int().min(0), y: z.number().int().min(0), values: alphaMapSchema,
}).strict().superRefine(validateObjectReference);

const treeInstanceSchema = z.object({
  prefab: assetReferenceSchema,
  position: vector3Schema,
  widthScale: z.number().positive().max(1000).default(1),
  heightScale: z.number().positive().max(1000).default(1),
  rotation: z.number().finite().default(0),
  color: colorSchema.optional(),
  lightmapColor: colorSchema.optional(),
}).strict();
export const addTerrainTreesSchema = z.object({
  ...objectReferenceShape,
  trees: z.array(treeInstanceSchema).min(1).max(2000),
}).strict().superRefine(validateObjectReference);

export const setSelectionSchema = z.object({
  objects: z.array(z.union([objectReferenceSchema, assetReferenceSchema])).max(256),
  activeIndex: z.number().int().min(0).max(255).default(0),
}).strict();
export const pingAssetSchema = z.object(assetReferenceShape).strict().superRefine(validateAssetReference);

export type EmptyParams = z.infer<typeof emptyParamsSchema>;
