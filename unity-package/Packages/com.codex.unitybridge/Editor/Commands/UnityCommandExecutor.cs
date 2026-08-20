using System;
using System.Collections.Generic;
using System.Text;
using Codex.UnityBridge.Connection;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal sealed class UnityCommandExecutor
    {
        private static readonly HashSet<string> MutatingCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "refresh_assets", "request_script_compilation", "create_game_object", "create_primitive",
            "duplicate_game_object", "delete_game_object", "reparent_game_object", "set_game_object_properties",
            "set_transform", "add_component", "remove_component", "set_component_property",
            "set_component_properties", "resize_serialized_array", "set_serialized_array_element", "import_asset",
            "instantiate_prefab", "save_game_object_as_prefab", "apply_prefab_instance", "revert_prefab_instance",
            "create_material", "set_material_property", "create_scriptable_object", "new_scene", "open_scene",
            "save_scene", "save_scene_as", "close_scene", "set_active_scene", "enter_play_mode", "exit_play_mode",
            "pause_play_mode", "step_frame", "clear_console_buffer", "batch", "batch_instantiate_prefab",
            "batch_set_transforms", "scatter_prefab", "create_terrain", "set_terrain_heights",
            "set_terrain_heights_patch", "set_terrain_layers", "set_terrain_alphamap_patch", "add_terrain_trees",
            "set_selection"
        };

        private readonly UnityWebSocketConnection connection;
        private readonly UnityConsoleBuffer consoleBuffer;

        public UnityCommandExecutor(UnityWebSocketConnection connection, UnityConsoleBuffer consoleBuffer)
        {
            this.connection = connection;
            this.consoleBuffer = consoleBuffer;
        }

        public void Execute(UnityCommandRequest request)
        {
            string response;
            try { response = ProtocolResponse.Success(request.RequestId, ExecuteCommand(request)); }
            catch (ProtocolException exception)
            {
                response = ProtocolResponse.Failure(request.RequestId, exception.Code, exception.Message, exception.Details);
            }
            catch (Exception exception)
            {
                response = ProtocolResponse.Failure(request.RequestId, "INTERNAL_ERROR",
                    "Unity command failed unexpectedly.",
                    new Dictionary<string, object> { { "exceptionType", exception.GetType().FullName } });
                Debug.LogException(exception);
            }
            int responseBytes = Encoding.UTF8.GetByteCount(response);
            if (responseBytes > connection.MaxMessageBytes)
            {
                response = ProtocolResponse.Failure(request.RequestId, "RESULT_TOO_LARGE",
                    "Unity command result exceeded the configured WebSocket payload limit. Request a smaller page or lower bounds.",
                    new Dictionary<string, object>
                    {
                        { "responseBytes", responseBytes }, { "maxMessageBytes", connection.MaxMessageBytes }
                    });
            }
            connection.EnqueueResponse(response);
        }

        private object ExecuteCommand(UnityCommandRequest request)
        {
            if (MutatingCommands.Contains(request.Command))
            {
                if (EditorApplication.isCompiling)
                    throw new ProtocolException("UNITY_COMPILING", "Unity is compiling scripts. Wait for the Bridge to reconnect, then retry.");
                if (EditorApplication.isUpdating && request.Command != "refresh_assets")
                    throw new ProtocolException("UNITY_BUSY", "Unity is updating the Asset Database. Use unity_wait_for_ready, then retry.");
            }

            if (StatusInspectionCommands.Handles(request.Command))
                return StatusInspectionCommands.Execute(request.Command, request.Parameters, connection);
            if (GameObjectCommands.Handles(request.Command))
                return GameObjectCommands.Execute(request.Command, request.Parameters);
            if (AssetCommands.Handles(request.Command))
                return AssetCommands.Execute(request.Command, request.Parameters);
            if (PrefabMaterialCommands.Handles(request.Command))
                return PrefabMaterialCommands.Execute(request.Command, request.Parameters);
            if (ScenePlayCaptureCommands.Handles(request.Command))
                return ScenePlayCaptureCommands.Execute(request.Command, request.Parameters);
            if (BatchTerrainCommands.Handles(request.Command))
                return BatchTerrainCommands.Execute(request.Command, request.Parameters);
            if (request.Command == "get_console") return consoleBuffer.Read(request.Parameters);
            if (request.Command == "clear_console_buffer") return consoleBuffer.Clear();
            throw new ProtocolException("COMMAND_FAILED",
                "Command '" + request.Command + "' is not implemented by this Bridge build.");
        }
    }
}
