using System;
using System.Collections.Generic;
using System.IO;
using Codex.UnityBridge.Connection;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Commands
{
    internal static class StatusInspectionCommands
    {
        public static object Execute(
            string command,
            IDictionary<string, object> parameters,
            UnityWebSocketConnection connection)
        {
            switch (command)
            {
                case "get_status": return GetStatus(connection);
                case "refresh_assets": return RefreshAssets(parameters);
                case "request_script_compilation": return RequestScriptCompilation();
                case "get_hierarchy": return GetHierarchy(parameters);
                case "get_game_object": return UnityObjectSerializer.DetailedGameObject(GameObjectResolver.Resolve(parameters));
                case "find_game_objects": return FindGameObjects(parameters);
                case "get_component_properties": return GetComponentProperties(parameters);
                case "get_project_info": return GetProjectInfo();
                case "get_open_scenes": return GetOpenScenes();
                case "get_build_settings": return GetBuildSettings();
                case "get_quality_settings": return GetQualitySettings();
                case "get_player_settings_summary": return GetPlayerSettingsSummary();
                case "get_packages": return GetPackages();
                default: return null;
            }
        }

        private static object GetHierarchy(IDictionary<string, object> parameters)
        {
            int maxDepth = CommandArguments.OptionalInt(parameters, "maxDepth", 16);
            int maxItems = CommandArguments.OptionalInt(parameters, "maxItems", 500);
            if (maxDepth < 0 || maxDepth > 64 || maxItems < 1 || maxItems > 1000)
                throw new ProtocolException("INVALID_ARGUMENT", "maxDepth or maxItems is outside the supported range.");
            return UnityObjectSerializer.HierarchySnapshot(maxDepth, maxItems);
        }

        public static bool Handles(string command)
        {
            return command == "get_status" || command == "refresh_assets"
                || command == "request_script_compilation" || command == "get_hierarchy"
                || command == "get_game_object" || command == "find_game_objects"
                || command == "get_component_properties" || command == "get_project_info"
                || command == "get_open_scenes" || command == "get_build_settings"
                || command == "get_quality_settings" || command == "get_player_settings_summary"
                || command == "get_packages";
        }

        public static IDictionary<string, object> GetStatus(UnityWebSocketConnection connection)
        {
            Scene scene = SceneManager.GetActiveScene();
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return new Dictionary<string, object>
            {
                { "connected", connection.IsConnected }, { "unityVersion", Application.unityVersion },
                { "projectName", new DirectoryInfo(ProjectPathUtility.ProjectRoot).Name },
                { "projectPath", ProjectPathUtility.ProjectRoot },
                { "currentScene", scene.name }, { "scenePath", scene.path },
                { "isSceneDirty", scene.IsValid() && scene.isDirty },
                { "isCompiling", EditorApplication.isCompiling },
                { "isUpdating", EditorApplication.isUpdating },
                { "isPlaying", EditorApplication.isPlaying },
                { "isPaused", EditorApplication.isPaused },
                { "isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode },
                { "playModeStatus", EditorApplication.isPlaying
                    ? (EditorApplication.isPaused ? "paused" : "playing") : "stopped" },
                { "playModeTransition", EditorStateTracker.PlayModeTransition },
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "renderPipeline", pipeline == null ? "Built-in Render Pipeline" : pipeline.GetType().FullName },
                { "renderPipelineAsset", pipeline == null ? null : AssetResolver.Reference(pipeline) },
                { "openSceneCount", SceneManager.sceneCount }
            };
        }

        private static object RefreshAssets(IDictionary<string, object> parameters)
        {
            ImportAssetOptions options = CommandArguments.OptionalBool(parameters, "forceUpdate", false)
                ? ImportAssetOptions.ForceUpdate : ImportAssetOptions.Default;
            AssetDatabase.Refresh(options);
            return new Dictionary<string, object>
            {
                { "requested", true }, { "forceUpdate", options == ImportAssetOptions.ForceUpdate },
                { "isCompiling", EditorApplication.isCompiling }, { "isUpdating", EditorApplication.isUpdating },
                { "message", "Refresh was requested. Call unity_wait_for_ready; refresh completion is not implied." }
            };
        }

        private static object RequestScriptCompilation()
        {
            CompilationPipeline.RequestScriptCompilation();
            return new Dictionary<string, object>
            {
                { "requested", true }, { "isCompiling", EditorApplication.isCompiling },
                { "message", "Compilation was requested. Call unity_wait_for_ready and then inspect Console errors." }
            };
        }

        private static object GetComponentProperties(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            int maxDepth = CommandArguments.OptionalInt(parameters, "maxDepth", 4);
            int maxItems = CommandArguments.OptionalInt(parameters, "maxItems", 200);
            bool includeHidden = CommandArguments.OptionalBool(parameters, "includeHidden", false);
            if (maxDepth < 0 || maxDepth > 16 || maxItems < 1 || maxItems > 1000)
                throw new ProtocolException("INVALID_ARGUMENT", "maxDepth or maxItems is outside the supported range.");
            return SerializedPropertySerializer.ComponentProperties(component, maxDepth, maxItems, includeHidden);
        }

        private static object FindGameObjects(IDictionary<string, object> parameters)
        {
            string exactName = CommandArguments.OptionalString(parameters, "name");
            string partialName = CommandArguments.OptionalString(parameters, "partialName");
            string componentName = CommandArguments.OptionalString(parameters, "componentType");
            string tag = CommandArguments.OptionalString(parameters, "tag");
            string scenePath = CommandArguments.OptionalString(parameters, "scenePath");
            bool includeInactive = CommandArguments.OptionalBool(parameters, "includeInactive", true);
            bool? active = null;
            object activeValue;
            if (parameters.TryGetValue("active", out activeValue) && activeValue != null) active = (bool)activeValue;
            int layer = ResolveOptionalLayer(parameters);
            Type componentType = componentName == null ? null : ComponentResolver.ResolveType(componentName);
            int offset = CommandArguments.OptionalInt(parameters, "offset", 0);
            int limit = CommandArguments.OptionalInt(parameters, "limit", 100);
            if (offset < 0 || limit < 1 || limit > 500)
                throw new ProtocolException("INVALID_ARGUMENT", "offset/limit is outside the supported range.");

            List<object> results = new List<object>();
            int matched = 0;
            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) continue;
                if (!includeInactive && !gameObject.activeInHierarchy) continue;
                if (active.HasValue && gameObject.activeSelf != active.Value) continue;
                if (exactName != null && !string.Equals(gameObject.name, exactName, StringComparison.OrdinalIgnoreCase)) continue;
                if (partialName != null && gameObject.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (componentType != null && gameObject.GetComponent(componentType) == null) continue;
                if (tag != null)
                {
                    try { if (!gameObject.CompareTag(tag)) continue; }
                    catch (UnityException) { throw new ProtocolException("INVALID_ARGUMENT", "Tag '" + tag + "' is not defined."); }
                }
                if (layer >= 0 && gameObject.layer != layer) continue;
                if (scenePath != null && !string.Equals(gameObject.scene.path, scenePath, StringComparison.Ordinal)) continue;
                if (matched++ < offset) continue;
                if (results.Count < limit) results.Add(UnityObjectSerializer.BasicGameObject(gameObject));
            }
            return new Dictionary<string, object>
            {
                { "results", results }, { "returned", results.Count }, { "totalMatches", matched },
                { "offset", offset }, { "limit", limit }, { "truncated", matched > offset + results.Count }
            };
        }

        private static int ResolveOptionalLayer(IDictionary<string, object> parameters)
        {
            object value;
            if (!parameters.TryGetValue("layer", out value) || value == null) return -1;
            string name = value as string;
            int layer = name == null ? CommandArguments.ToInt(value, "layer") : LayerMask.NameToLayer(name);
            if (layer < 0 || layer > 31) throw new ProtocolException("INVALID_ARGUMENT", "Layer is not defined.");
            return layer;
        }

        private static object GetProjectInfo()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return new Dictionary<string, object>
            {
                { "projectName", new DirectoryInfo(ProjectPathUtility.ProjectRoot).Name },
                { "projectPath", ProjectPathUtility.ProjectRoot }, { "unityVersion", Application.unityVersion },
                { "companyName", PlayerSettings.companyName }, { "productName", PlayerSettings.productName },
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "colorSpace", PlayerSettings.colorSpace.ToString() },
                { "renderPipeline", pipeline == null ? "Built-in Render Pipeline" : pipeline.GetType().FullName },
                { "assetsPath", Application.dataPath }, { "openScenes", OpenSceneValues() }
            };
        }

        private static object GetOpenScenes()
        {
            List<object> scenes = OpenSceneValues();
            return new Dictionary<string, object> { { "count", scenes.Count }, { "scenes", scenes } };
        }

        private static List<object> OpenSceneValues()
        {
            List<object> scenes = new List<object>();
            for (int index = 0; index < SceneManager.sceneCount; index++) scenes.Add(SceneResolver.Serialize(SceneManager.GetSceneAt(index)));
            return scenes;
        }

        private static object GetBuildSettings()
        {
            List<object> scenes = new List<object>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                scenes.Add(new Dictionary<string, object> { { "path", scene.path }, { "enabled", scene.enabled }, { "guid", scene.guid.ToString() } });
            return new Dictionary<string, object>
            {
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() }, { "scenes", scenes }
            };
        }

        private static object GetQualitySettings()
        {
            return new Dictionary<string, object>
            {
                { "currentLevel", QualitySettings.GetQualityLevel() }, { "currentName", QualitySettings.names[QualitySettings.GetQualityLevel()] },
                { "levels", QualitySettings.names }, { "vSyncCount", QualitySettings.vSyncCount },
                { "antiAliasing", QualitySettings.antiAliasing }, { "shadowDistance", QualitySettings.shadowDistance }
            };
        }

        private static object GetPlayerSettingsSummary()
        {
            return new Dictionary<string, object>
            {
                { "companyName", PlayerSettings.companyName }, { "productName", PlayerSettings.productName },
                { "version", PlayerSettings.bundleVersion }, { "colorSpace", PlayerSettings.colorSpace.ToString() },
                { "defaultScreenWidth", PlayerSettings.defaultScreenWidth }, { "defaultScreenHeight", PlayerSettings.defaultScreenHeight }
            };
        }

        private static object GetPackages()
        {
            List<object> packages = new List<object>();
            foreach (UnityEditor.PackageManager.PackageInfo package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                packages.Add(new Dictionary<string, object>
                {
                    { "name", package.name }, { "displayName", package.displayName }, { "version", package.version },
                    { "source", package.source.ToString() }, { "resolvedPath", package.resolvedPath }
                });
            return new Dictionary<string, object> { { "count", packages.Count }, { "packages", packages } };
        }
    }
}
