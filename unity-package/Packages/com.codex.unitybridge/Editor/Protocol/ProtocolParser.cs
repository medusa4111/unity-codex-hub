using System;
using System.Collections.Generic;

namespace Codex.UnityBridge.Protocol
{
    internal static class ProtocolParser
    {
        private static readonly HashSet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
        {
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
            "get_packages"
        };

        public static UnityCommandRequest ParseRequest(string json)
        {
            object parsed = Json.Deserialize(json);
            IDictionary<string, object> message = parsed as IDictionary<string, object>;
            if (message == null)
            {
                throw new ProtocolException("INVALID_ARGUMENT", "Command message must be a JSON object.");
            }

            string requestId = ReadRequiredString(message, "requestId");
            Guid ignored;
            if (!Guid.TryParse(requestId, out ignored))
            {
                throw new ProtocolException("INVALID_ARGUMENT", "requestId must be a UUID.");
            }

            string command = ReadRequiredString(message, "command");
            if (!AllowedCommands.Contains(command))
            {
                throw new ProtocolException("INVALID_ARGUMENT", "Command is not in the Unity Bridge allowlist.");
            }

            object parametersValue;
            IDictionary<string, object> parameters = new Dictionary<string, object>();
            if (message.TryGetValue("params", out parametersValue))
            {
                parameters = parametersValue as IDictionary<string, object>;
                if (parameters == null)
                {
                    throw new ProtocolException("INVALID_ARGUMENT", "params must be a JSON object.");
                }
            }

            return new UnityCommandRequest(requestId, command, parameters);
        }

        private static string ReadRequiredString(IDictionary<string, object> source, string name)
        {
            object value;
            if (!source.TryGetValue(name, out value) || !(value is string) || string.IsNullOrEmpty((string)value))
            {
                throw new ProtocolException("INVALID_ARGUMENT", name + " must be a non-empty string.");
            }
            return (string)value;
        }
    }
}
