import { z } from "zod";
import { ERROR_CODES, type ProtocolError } from "./error.js";

export const PROTOCOL_VERSION = 3;

export const UNITY_COMMANDS = [
  "get_status",
  "refresh_assets",
  "request_script_compilation",
  "get_hierarchy",
  "get_game_object",
  "find_game_objects",
  "get_component_properties",
  "get_project_info",
  "get_open_scenes",
  "create_game_object",
  "create_primitive",
  "duplicate_game_object",
  "delete_game_object",
  "reparent_game_object",
  "set_game_object_properties",
  "set_transform",
  "add_component",
  "remove_component",
  "set_component_property",
  "set_component_properties",
  "resize_serialized_array",
  "set_serialized_array_element",
  "find_assets",
  "get_asset_info",
  "get_asset_dependencies",
  "import_asset",
  "get_asset_preview",
  "instantiate_prefab",
  "get_prefab_info",
  "save_game_object_as_prefab",
  "apply_prefab_instance",
  "revert_prefab_instance",
  "create_material",
  "get_material_properties",
  "set_material_property",
  "create_scriptable_object",
  "list_scenes",
  "new_scene",
  "open_scene",
  "get_console",
  "clear_console_buffer",
  "save_scene",
  "save_scene_as",
  "close_scene",
  "set_active_scene",
  "enter_play_mode",
  "exit_play_mode",
  "pause_play_mode",
  "step_frame",
  "capture_game_view",
  "capture_camera",
  "capture_scene_view",
  "batch",
  "batch_instantiate_prefab",
  "batch_set_transforms",
  "scatter_prefab",
  "create_terrain",
  "get_terrain_info",
  "set_terrain_heights",
  "set_terrain_heights_patch",
  "set_terrain_layers",
  "set_terrain_alphamap_patch",
  "add_terrain_trees",
  "get_selection",
  "set_selection",
  "frame_object_in_scene_view",
  "ping_asset",
  "get_build_settings",
  "get_quality_settings",
  "get_player_settings_summary",
  "get_packages",
] as const;

export type UnityCommand = (typeof UNITY_COMMANDS)[number];

export interface UnityCommandRequest {
  requestId: string;
  command: UnityCommand;
  params: Record<string, unknown>;
}

export interface UnityCommandSuccess {
  requestId: string;
  success: true;
  result: unknown;
  error: null;
}

export interface UnityCommandFailure {
  requestId: string;
  success: false;
  result: null;
  error: ProtocolError;
}

export type UnityCommandResponse = UnityCommandSuccess | UnityCommandFailure;

export interface UnityHello {
  type: "unity_hello";
  protocolVersion: number;
  unityVersion: string;
  projectName: string;
  projectPath: string;
  currentScene: string;
}

export interface HubHello {
  type: "hub_hello";
  protocolVersion: number;
}

const errorSchema = z.object({
  code: z.enum(ERROR_CODES),
  message: z.string().min(1),
  details: z.unknown().optional(),
});

export const unityHelloSchema = z.object({
  type: z.literal("unity_hello"),
  protocolVersion: z.number().int().positive(),
  unityVersion: z.string(),
  projectName: z.string(),
  projectPath: z.string(),
  currentScene: z.string(),
});

export const unityCommandResponseSchema = z.discriminatedUnion("success", [
  z.object({
    requestId: z.uuid(),
    success: z.literal(true),
    result: z.unknown(),
    error: z.null(),
  }),
  z.object({
    requestId: z.uuid(),
    success: z.literal(false),
    result: z.null(),
    error: errorSchema,
  }),
]);

export function isUnityCommand(value: string): value is UnityCommand {
  return (UNITY_COMMANDS as readonly string[]).includes(value);
}
