import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { z } from "zod";
import { readGeneratedCapture } from "../capture/captureReader.js";
import type { Logger } from "../logging/logger.js";
import { HubError } from "../protocol/error.js";
import type { UnityCommand } from "../protocol/messages.js";
import {
  addComponentSchema,
  addTerrainTreesSchema,
  assetDependenciesSchema,
  assetInfoSchema,
  assetPreviewSchema,
  batchInstantiatePrefabSchema,
  batchSchema,
  batchSetTransformsSchema,
  captureCameraSchema,
  captureGameViewSchema,
  captureSceneViewSchema,
  closeSceneSchema,
  componentPropertiesSchema,
  createGameObjectSchema,
  createMaterialSchema,
  createPrimitiveSchema,
  createScriptableObjectSchema,
  createTerrainSchema,
  duplicateGameObjectSchema,
  emptyParamsSchema,
  findAssetsSchema,
  findGameObjectsSchema,
  getConsoleSchema,
  hierarchySchema,
  importAssetSchema,
  instantiatePrefabSchema,
  listScenesSchema,
  materialPropertiesSchema,
  newSceneSchema,
  objectReferenceSchema,
  openSceneSchema,
  pausePlayModeSchema,
  pingAssetSchema,
  prefabInfoSchema,
  prefabMutationSchema,
  refreshAssetsSchema,
  removeComponentSchema,
  reparentGameObjectSchema,
  resizeSerializedArraySchema,
  savePrefabSchema,
  saveSceneAsSchema,
  saveSceneSchema,
  scatterPrefabSchema,
  sceneReferenceSchema,
  setComponentPropertiesSchema,
  setComponentPropertySchema,
  setGameObjectPropertiesSchema,
  setMaterialPropertySchema,
  setSelectionSchema,
  setSerializedArrayElementSchema,
  setTerrainAlphamapPatchSchema,
  setTerrainHeightsPatchSchema,
  setTerrainHeightsSchema,
  setTerrainLayersSchema,
  setTransformSchema,
  terrainReferenceSchema,
  waitForPlayModeSchema,
  waitSchema,
} from "../protocol/schemas.js";
import type { UnityConnection } from "../websocket/unityConnection.js";
import { toolFailure, toolImageSuccess, toolSuccess } from "./toolResult.js";

const SERVER_INSTRUCTIONS =
  "Inspect before modifying: start multi-step work with unity_status, inspect Scenes/components, and search existing " +
  "assets before inventing substitutes. Prefer Prefab instantiation when an asset exists and batch tools for repeated " +
  "work. After external script edits, refresh assets, wait for ready through Domain Reload, then inspect Console. " +
  "After visual changes or Play Mode entry, wait for the actual state, capture an image, inspect Console, and iterate; " +
  "never claim success unless tools confirmed it. Prefer returned instanceId values over names. Scene changes never " +
  "discard dirty Scenes unless discardModified=true is explicit. Asset writes are limited to normalized Assets/ paths. " +
  "Mutations use Unity Undo where supported; responses identify non-undoable asset writes.";

interface ToolAnnotations {
  readOnlyHint: boolean;
  destructiveHint: boolean;
  idempotentHint: boolean;
  openWorldHint: false;
}

interface BridgeTool {
  name: string;
  title: string;
  description: string;
  schema: z.ZodType;
  command: UnityCommand;
  annotations: ToolAnnotations;
  image?: true;
}

type LooseRegisterTool = (
  name: string,
  config: {
    title: string;
    description: string;
    inputSchema: z.ZodType;
    annotations: ToolAnnotations;
  },
  callback: (args: Record<string, unknown>) => Promise<CallToolResult>,
) => void;

const readOnly = annotations(true, false, true);
const observe = annotations(true, false, false);
const create = annotations(false, false, false);
const update = annotations(false, false, true);
const destructive = annotations(false, true, false);

const TOOLS: readonly BridgeTool[] = [
  tool("unity_refresh_assets", "Refresh Asset Database", "Refresh Unity's Asset Database. Then call unity_wait_for_ready.", refreshAssetsSchema, "refresh_assets", update),
  tool("unity_request_script_compilation", "Request script compilation", "Ask Unity to compile scripts. A Domain Reload may disconnect the Bridge; call unity_wait_for_ready.", emptyParamsSchema, "request_script_compilation", create),
  tool("unity_get_hierarchy", "Get scene hierarchy", "Return a bounded active-scene hierarchy with stable object IDs, paths, components, and truncation metadata.", hierarchySchema, "get_hierarchy", readOnly),
  tool("unity_get_game_object", "Inspect GameObject", "Inspect one active-scene GameObject, its transforms, flags, components, Scene, and Prefab state.", objectReferenceSchema, "get_game_object", readOnly),
  tool("unity_find_game_objects", "Find GameObjects", "Find active or inactive loaded-scene GameObjects by name, component, tag, layer, state, and Scene.", findGameObjectsSchema, "find_game_objects", readOnly),
  tool("unity_get_component_properties", "Inspect component properties", "Enumerate bounded serialized properties, including arrays and safe scene/asset object references.", componentPropertiesSchema, "get_component_properties", readOnly),
  tool("unity_get_project_info", "Get project information", "Return project, Unity version, build target, render pipeline, and open Scene information.", emptyParamsSchema, "get_project_info", readOnly),
  tool("unity_get_open_scenes", "Get open Scenes", "List open Scenes with active, loaded, dirty, and root-count state.", emptyParamsSchema, "get_open_scenes", readOnly),

  tool("unity_create_game_object", "Create GameObject", "Create an empty GameObject, optionally parented, with Unity Undo support.", createGameObjectSchema, "create_game_object", create),
  tool("unity_create_primitive", "Create primitive", "Create a Unity primitive with optional parent and local transform, with Undo support.", createPrimitiveSchema, "create_primitive", create),
  tool("unity_duplicate_game_object", "Duplicate GameObject", "Duplicate a GameObject and optionally reparent, rename, or override its local transform.", duplicateGameObjectSchema, "duplicate_game_object", create),
  tool("unity_delete_game_object", "Delete GameObject", "Delete a GameObject and its descendants using Unity Undo.", objectReferenceSchema, "delete_game_object", destructive),
  tool("unity_reparent_game_object", "Reparent GameObject", "Move a GameObject in the hierarchy while explicitly choosing local- or world-transform preservation.", reparentGameObjectSchema, "reparent_game_object", update),
  tool("unity_set_game_object_properties", "Set GameObject properties", "Set name, active state, tag, layer, or static flags using Unity Undo.", setGameObjectPropertiesSchema, "set_game_object_properties", update),
  tool("unity_set_transform", "Set transform", "Set local or world position/rotation and local scale using Unity Undo.", setTransformSchema, "set_transform", update),
  tool("unity_add_component", "Add Component", "Add a concrete Component by exact or unambiguous type name using Unity Undo.", addComponentSchema, "add_component", create),
  tool("unity_remove_component", "Remove Component", "Remove a non-Transform Component using Unity Undo.", removeComponentSchema, "remove_component", destructive),
  tool("unity_set_component_property", "Set component property", "Set one exact serialized property, including supported object references, using Unity Undo.", setComponentPropertySchema, "set_component_property", update),
  tool("unity_set_component_properties", "Set component properties", "Set several serialized properties in one Undo group.", setComponentPropertiesSchema, "set_component_properties", update),
  tool("unity_resize_serialized_array", "Resize serialized array", "Resize one serialized array property with bounded size and Unity Undo.", resizeSerializedArraySchema, "resize_serialized_array", update),
  tool("unity_set_serialized_array_element", "Set serialized array element", "Set one element in an existing serialized array using Unity Undo.", setSerializedArrayElementSchema, "set_serialized_array_element", update),

  tool("unity_find_assets", "Find assets", "Search the Asset Database with optional type, folder, offset, and limit filters.", findAssetsSchema, "find_assets", readOnly),
  tool("unity_get_asset_info", "Inspect asset", "Inspect an asset by Assets path or GUID with type-specific metadata and optional dependencies.", assetInfoSchema, "get_asset_info", readOnly),
  tool("unity_get_asset_dependencies", "Get asset dependencies", "Return paginated Asset Database dependencies for one asset.", assetDependenciesSchema, "get_asset_dependencies", readOnly),
  tool("unity_import_asset", "Import asset", "Import an existing normalized Assets path with explicit synchronous/force options.", importAssetSchema, "import_asset", update),
  imageTool("unity_get_asset_preview", "Preview asset", "Render a bounded Unity Asset Preview and return it as MCP PNG image content.", assetPreviewSchema, "get_asset_preview", observe),

  tool("unity_instantiate_prefab", "Instantiate Prefab", "Instantiate a Prefab by path or GUID with optional parent, name, and local transform.", instantiatePrefabSchema, "instantiate_prefab", create),
  tool("unity_get_prefab_info", "Inspect Prefab instance", "Inspect Prefab source, instance status, root, and override count for a GameObject.", prefabInfoSchema, "get_prefab_info", readOnly),
  tool("unity_save_game_object_as_prefab", "Save GameObject as Prefab", "Create or explicitly overwrite a Prefab asset from a scene GameObject.", savePrefabSchema, "save_game_object_as_prefab", annotations(false, true, false)),
  tool("unity_apply_prefab_instance", "Apply Prefab overrides", "Apply the outermost Prefab instance overrides to its asset.", prefabMutationSchema, "apply_prefab_instance", annotations(false, true, false)),
  tool("unity_revert_prefab_instance", "Revert Prefab overrides", "Revert the outermost Prefab instance to its source asset using Unity Undo.", prefabMutationSchema, "revert_prefab_instance", annotations(false, true, false)),
  tool("unity_create_material", "Create Material", "Create or explicitly overwrite a Material asset with a named Shader.", createMaterialSchema, "create_material", annotations(false, true, false)),
  tool("unity_get_material_properties", "Inspect Material", "Return public Shader properties and current Material values.", materialPropertiesSchema, "get_material_properties", readOnly),
  tool("unity_set_material_property", "Set Material property", "Set a public Color, Vector, number, Integer, or Texture property with Undo support.", setMaterialPropertySchema, "set_material_property", update),
  tool("unity_create_scriptable_object", "Create ScriptableObject", "Create or explicitly overwrite a concrete ScriptableObject asset and initialize serialized properties.", createScriptableObjectSchema, "create_scriptable_object", annotations(false, true, false)),

  tool("unity_list_scenes", "List Scene assets", "List Scene assets with build-settings and currently-open state.", listScenesSchema, "list_scenes", readOnly),
  tool("unity_new_scene", "Create new Scene", "Create an empty/default Scene. Dirty Scenes require explicit saveModified or discardModified policy.", newSceneSchema, "new_scene", annotations(false, true, false)),
  tool("unity_open_scene", "Open Scene", "Open a Scene additively or singly. Dirty Scenes require an explicit policy before Single mode.", openSceneSchema, "open_scene", annotations(false, true, false)),
  tool("unity_save_scene", "Save Scene", "Save the active or identified open Scene to its existing asset path.", saveSceneSchema, "save_scene", update),
  tool("unity_save_scene_as", "Save Scene as", "Save an open Scene to a new normalized Assets/*.unity path with explicit overwrite.", saveSceneAsSchema, "save_scene_as", annotations(false, true, false)),
  tool("unity_close_scene", "Close Scene", "Close a loaded Scene; dirty content requires explicit save or discard policy.", closeSceneSchema, "close_scene", annotations(false, true, false)),
  tool("unity_set_active_scene", "Set active Scene", "Set one loaded Scene as active.", sceneReferenceSchema, "set_active_scene", update),

  tool("unity_enter_play_mode", "Enter Play Mode", "Request Play Mode entry, then use unity_wait_for_play_mode.", emptyParamsSchema, "enter_play_mode", create),
  tool("unity_exit_play_mode", "Exit Play Mode", "Request Play Mode exit, then use unity_wait_for_play_mode.", emptyParamsSchema, "exit_play_mode", create),
  tool("unity_pause_play_mode", "Pause or resume Play Mode", "Pause or resume an active Play Mode session.", pausePlayModeSchema, "pause_play_mode", update),
  tool("unity_step_frame", "Step one frame", "Advance one frame while Unity is playing and paused.", emptyParamsSchema, "step_frame", create),
  imageTool("unity_capture_game_view", "Capture Game View", "Render the active Game camera at bounded dimensions and return MCP PNG image content.", captureGameViewSchema, "capture_game_view", observe),
  imageTool("unity_capture_camera", "Capture Camera", "Render a referenced Camera component and return MCP PNG image content.", captureCameraSchema, "capture_camera", observe),
  imageTool("unity_capture_scene_view", "Capture Scene View", "Render the active Scene View camera and return MCP PNG image content.", captureSceneViewSchema, "capture_scene_view", observe),

  tool("unity_get_console", "Read Console buffer", "Read filtered, sequenced messages captured by the Bridge, with bounded results and optional stacks.", getConsoleSchema, "get_console", readOnly),
  tool("unity_clear_console_buffer", "Clear Bridge Console buffer", "Clear only the Bridge's captured log buffer; the Unity Console window is unchanged.", emptyParamsSchema, "clear_console_buffer", update),
  tool("unity_batch", "Run safe batch", "Run up to 100 allowlisted GameObject operations in one Unity Undo group with per-item results.", batchSchema, "batch", annotations(false, true, false)),
  tool("unity_batch_instantiate_prefab", "Batch instantiate Prefab", "Instantiate up to 500 placements of one Prefab in one Undo group.", batchInstantiatePrefabSchema, "batch_instantiate_prefab", create),
  tool("unity_batch_set_transforms", "Batch set transforms", "Apply up to 1000 transform updates in one Undo group with per-item errors.", batchSetTransformsSchema, "batch_set_transforms", update),
  tool("unity_scatter_prefab", "Scatter Prefab", "Deterministically scatter a Prefab in a box or disk with seed, spacing, scale, yaw, and optional surface alignment.", scatterPrefabSchema, "scatter_prefab", create),

  tool("unity_create_terrain", "Create Terrain", "Create TerrainData under Assets and its scene Terrain GameObject with bounded resolutions.", createTerrainSchema, "create_terrain", annotations(false, true, false)),
  tool("unity_get_terrain_info", "Inspect Terrain", "Inspect TerrainData dimensions, layers, and tree counts for a Terrain GameObject.", terrainReferenceSchema, "get_terrain_info", readOnly),
  tool("unity_set_terrain_heights", "Set Terrain heights", "Replace the complete normalized heightmap; dimensions must match the Terrain resolution.", setTerrainHeightsSchema, "set_terrain_heights", update),
  tool("unity_set_terrain_heights_patch", "Patch Terrain heights", "Set a bounded normalized rectangular height patch.", setTerrainHeightsPatchSchema, "set_terrain_heights_patch", update),
  tool("unity_set_terrain_layers", "Set Terrain layers", "Replace TerrainLayer asset references using Unity Undo.", setTerrainLayersSchema, "set_terrain_layers", update),
  tool("unity_set_terrain_alphamap_patch", "Patch Terrain alphamap", "Set and normalize a bounded alphamap patch matching the Terrain layer count.", setTerrainAlphamapPatchSchema, "set_terrain_alphamap_patch", update),
  tool("unity_add_terrain_trees", "Add Terrain trees", "Add bounded normalized TreeInstances and register missing Prefab prototypes.", addTerrainTreesSchema, "add_terrain_trees", create),

  tool("unity_get_selection", "Get Editor selection", "Return selected scene objects/assets and the active selection.", emptyParamsSchema, "get_selection", readOnly),
  tool("unity_set_selection", "Set Editor selection", "Select bounded scene objects or assets without using reflection.", setSelectionSchema, "set_selection", update),
  tool("unity_frame_object_in_scene_view", "Frame GameObject", "Select and frame one active-scene GameObject in the active Scene View.", objectReferenceSchema, "frame_object_in_scene_view", create),
  tool("unity_ping_asset", "Ping asset", "Ping one asset in the Unity Project window.", pingAssetSchema, "ping_asset", create),
  tool("unity_get_build_settings", "Get build settings", "Return active build target and configured build Scenes.", emptyParamsSchema, "get_build_settings", readOnly),
  tool("unity_get_quality_settings", "Get quality settings", "Return current quality level and core rendering quality values.", emptyParamsSchema, "get_quality_settings", readOnly),
  tool("unity_get_player_settings_summary", "Get Player settings", "Return a safe summary of public PlayerSettings values.", emptyParamsSchema, "get_player_settings_summary", readOnly),
  tool("unity_get_packages", "Get registered packages", "Return packages currently registered in the Unity project.", emptyParamsSchema, "get_packages", readOnly),
];

export function createMcpServer(connection: UnityConnection, logger: Logger): McpServer {
  const server = new McpServer(
    { name: "unity-codex-hub", version: "0.2.0" },
    { instructions: SERVER_INSTRUCTIONS },
  );
  const register = server.registerTool.bind(server) as unknown as LooseRegisterTool;

  register("unity_status", {
    title: "Unity Editor status",
    description: "Return local connection identity plus authoritative compilation, update, Scene, render-pipeline, and Play Mode state.",
    inputSchema: emptyParamsSchema,
    annotations: readOnly,
  }, async () => statusTool(connection, logger));

  register("unity_wait_for_ready", {
    title: "Wait for Unity readiness",
    description: "Wait through compilation, Asset Database update, Domain Reload disconnect, and reconnect until Unity is ready.",
    inputSchema: waitSchema,
    annotations: observe,
  }, async (args) => waitForReadyTool(connection, logger, numberArg(args, "timeoutMs", 120_000), numberArg(args, "pollIntervalMs", 250)));

  register("unity_wait_for_play_mode", {
    title: "Wait for Play Mode state",
    description: "Wait through Play Mode transition and possible Domain Reload until playing, paused, or stopped.",
    inputSchema: waitForPlayModeSchema,
    annotations: observe,
  }, async (args) => waitForPlayModeTool(
    connection,
    logger,
    stringArg(args, "state") as "playing" | "paused" | "stopped",
    numberArg(args, "timeoutMs", 120_000),
    numberArg(args, "pollIntervalMs", 250),
  ));

  for (const definition of TOOLS) {
    register(definition.name, {
      title: definition.title,
      description: definition.description,
      inputSchema: definition.schema,
      annotations: definition.annotations,
    }, async (args) => definition.image === true
      ? executeImageTool(connection, logger, definition.command, args)
      : executeTool(connection, logger, definition.command, args));
  }
  return server;
}

async function statusTool(connection: UnityConnection, logger: Logger): Promise<CallToolResult> {
  try {
    const local = connection.status();
    if (!local.connected) return toolSuccess(local);
    try { return toolSuccess(await connection.execute("get_status", {})); }
    catch (error) {
      if (error instanceof HubError && error.code === "UNITY_NOT_CONNECTED") return toolSuccess(connection.status());
      throw error;
    }
  } catch (error) {
    logger.error("unity_status failed", { error: error instanceof Error ? error.message : String(error) });
    return toolFailure(error);
  }
}

async function executeTool(
  connection: UnityConnection,
  logger: Logger,
  command: UnityCommand,
  params: Record<string, unknown>,
): Promise<CallToolResult> {
  try { return toolSuccess(await connection.execute(command, params)); }
  catch (error) {
    logger.warn("Unity tool failed", { command, error: error instanceof Error ? error.message : String(error) });
    return toolFailure(error);
  }
}

async function executeImageTool(
  connection: UnityConnection,
  logger: Logger,
  command: UnityCommand,
  params: Record<string, unknown>,
): Promise<CallToolResult> {
  try {
    const projectPath = connection.status().projectPath;
    const result = await connection.execute(command, params);
    if (isRecord(result) && result["ready"] === false) return toolSuccess(result);
    const capture = await readGeneratedCapture(result, projectPath);
    return toolImageSuccess(capture.metadata, capture.data, capture.mimeType);
  } catch (error) {
    logger.warn("Unity image tool failed", { command, error: error instanceof Error ? error.message : String(error) });
    return toolFailure(error);
  }
}

async function waitForReadyTool(
  connection: UnityConnection,
  logger: Logger,
  timeoutMs: number,
  pollIntervalMs: number,
): Promise<CallToolResult> {
  const deadline = Date.now() + timeoutMs;
  let lastStatus: unknown = connection.status();
  while (Date.now() <= deadline) {
    if (connection.status().connected) {
      try {
        lastStatus = await connection.execute("get_status", {});
        if (isReadyStatus(lastStatus)) return toolSuccess({ ready: true, status: lastStatus });
      } catch (error) {
        if (!(error instanceof HubError) || error.code !== "UNITY_NOT_CONNECTED") {
          logger.warn("Readiness poll failed", { error: error instanceof Error ? error.message : String(error) });
          lastStatus = error instanceof Error ? error.message : String(error);
        }
      }
    } else {
      lastStatus = connection.status();
    }
    await delay(Math.min(pollIntervalMs, Math.max(1, deadline - Date.now())));
  }
  return toolFailure(new HubError("TIMEOUT", `Unity was not ready within ${timeoutMs} ms`, { lastStatus }));
}

async function waitForPlayModeTool(
  connection: UnityConnection,
  logger: Logger,
  state: "playing" | "paused" | "stopped",
  timeoutMs: number,
  pollIntervalMs: number,
): Promise<CallToolResult> {
  const deadline = Date.now() + timeoutMs;
  let lastStatus: unknown = connection.status();
  while (Date.now() <= deadline) {
    if (connection.status().connected) {
      try {
        lastStatus = await connection.execute("get_status", {});
        if (matchesPlayMode(lastStatus, state)) return toolSuccess({ reached: state, status: lastStatus });
      } catch (error) {
        if (!(error instanceof HubError) || error.code !== "UNITY_NOT_CONNECTED") {
          logger.warn("Play Mode poll failed", { error: error instanceof Error ? error.message : String(error) });
          lastStatus = error instanceof Error ? error.message : String(error);
        }
      }
    } else {
      lastStatus = connection.status();
    }
    await delay(Math.min(pollIntervalMs, Math.max(1, deadline - Date.now())));
  }
  return toolFailure(new HubError("TIMEOUT", `Unity did not reach Play Mode state '${state}' within ${timeoutMs} ms`, { lastStatus }));
}

function isReadyStatus(value: unknown): boolean {
  if (!isRecord(value)) return false;
  return value["connected"] === true && value["isCompiling"] === false && value["isUpdating"] === false
    && value["playModeTransition"] !== "entering" && value["playModeTransition"] !== "exiting";
}

function matchesPlayMode(value: unknown, state: "playing" | "paused" | "stopped"): boolean {
  if (!isRecord(value) || value["isCompiling"] === true) return false;
  if (state === "paused") return value["isPlaying"] === true && value["isPaused"] === true;
  if (state === "playing") return value["isPlaying"] === true && value["isPaused"] === false;
  return value["isPlaying"] === false && value["isPlayingOrWillChangePlaymode"] === false;
}

function tool(
  name: string,
  title: string,
  description: string,
  schema: z.ZodType,
  command: UnityCommand,
  toolAnnotations: ToolAnnotations,
): BridgeTool {
  return { name, title, description, schema, command, annotations: toolAnnotations };
}

function imageTool(
  name: string,
  title: string,
  description: string,
  schema: z.ZodType,
  command: UnityCommand,
  toolAnnotations: ToolAnnotations,
): BridgeTool {
  return { ...tool(name, title, description, schema, command, toolAnnotations), image: true };
}

function annotations(readOnlyHint: boolean, destructiveHint: boolean, idempotentHint: boolean): ToolAnnotations {
  return { readOnlyHint, destructiveHint, idempotentHint, openWorldHint: false };
}

function numberArg(args: Record<string, unknown>, name: string, fallback: number): number {
  const value = args[name];
  return typeof value === "number" ? value : fallback;
}

function stringArg(args: Record<string, unknown>, name: string): string {
  const value = args[name];
  if (typeof value !== "string") throw new HubError("INVALID_ARGUMENT", `${name} must be a string`);
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
