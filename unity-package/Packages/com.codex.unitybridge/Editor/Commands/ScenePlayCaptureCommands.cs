using System;
using System.Collections.Generic;
using System.IO;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Commands
{
    internal static class ScenePlayCaptureCommands
    {
        public static bool Handles(string command)
        {
            return command == "list_scenes" || command == "new_scene" || command == "open_scene"
                || command == "save_scene" || command == "save_scene_as" || command == "close_scene"
                || command == "set_active_scene" || command == "enter_play_mode" || command == "exit_play_mode"
                || command == "pause_play_mode" || command == "step_frame" || command == "capture_game_view"
                || command == "capture_camera" || command == "capture_scene_view" || command == "get_selection"
                || command == "set_selection" || command == "frame_object_in_scene_view";
        }

        public static object Execute(string command, IDictionary<string, object> parameters)
        {
            switch (command)
            {
                case "list_scenes": return ListScenes(parameters);
                case "new_scene": return NewScene(parameters);
                case "open_scene": return OpenScene(parameters);
                case "save_scene": return SaveScene(parameters);
                case "save_scene_as": return SaveSceneAs(parameters);
                case "close_scene": return CloseScene(parameters);
                case "set_active_scene": return SetActiveScene(parameters);
                case "enter_play_mode": return EnterPlayMode();
                case "exit_play_mode": return ExitPlayMode();
                case "pause_play_mode": return PausePlayMode(parameters);
                case "step_frame": return StepFrame();
                case "capture_game_view": return CaptureGameView(parameters);
                case "capture_camera": return CaptureCamera(parameters);
                case "capture_scene_view": return CaptureSceneView(parameters);
                case "get_selection": return GetSelection();
                case "set_selection": return SetSelection(parameters);
                case "frame_object_in_scene_view": return FrameObject(parameters);
                default: return null;
            }
        }

        private static object ListScenes(IDictionary<string, object> parameters)
        {
            bool includePackages = CommandArguments.OptionalBool(parameters, "includePackages", false);
            int offset = CommandArguments.OptionalInt(parameters, "offset", 0);
            int limit = CommandArguments.OptionalInt(parameters, "limit", 200);
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            List<object> scenes = new List<object>();
            int matched = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!includePackages && !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (matched++ < offset) continue;
                if (scenes.Count >= limit) continue;
                scenes.Add(new Dictionary<string, object>
                {
                    { "name", Path.GetFileNameWithoutExtension(path) }, { "path", path }, { "guid", guid },
                    { "isOpen", IsSceneOpen(path) }, { "inBuildSettings", IsInBuildSettings(path) }
                });
            }
            return new Dictionary<string, object>
            {
                { "scenes", scenes }, { "returned", scenes.Count }, { "totalMatches", matched },
                { "offset", offset }, { "limit", limit }, { "truncated", matched > offset + scenes.Count }
            };
        }

        private static object NewScene(IDictionary<string, object> parameters)
        {
            string modeText = CommandArguments.OptionalString(parameters, "mode", "Single");
            if (modeText == "Single") HandleDirtyScenes(parameters);
            NewSceneSetup setup = ParseEnum<NewSceneSetup>(
                CommandArguments.OptionalString(parameters, "setup", "EmptyScene"), "setup");
            NewSceneMode mode = ParseEnum<NewSceneMode>(modeText, "mode");
            Scene scene = EditorSceneManager.NewScene(setup, mode);
            return new Dictionary<string, object> { { "created", true }, { "scene", SceneResolver.Serialize(scene) } };
        }

        private static object OpenScene(IDictionary<string, object> parameters)
        {
            string path = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "scenePath"), ".unity", "scenePath");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new ProtocolException("SCENE_NOT_FOUND", "Scene asset '" + path + "' was not found.");
            string modeText = CommandArguments.OptionalString(parameters, "mode", "Single");
            if (modeText == "Single") HandleDirtyScenes(parameters);
            OpenSceneMode mode = ParseEnum<OpenSceneMode>(modeText, "mode");
            Scene scene = EditorSceneManager.OpenScene(path, mode);
            return new Dictionary<string, object> { { "opened", true }, { "scene", SceneResolver.Serialize(scene) } };
        }

        private static object SaveScene(IDictionary<string, object> parameters)
        {
            Scene scene = SceneResolver.ResolveLoaded(parameters, true);
            if (!scene.IsValid()) throw new ProtocolException("SCENE_NOT_FOUND", "Unity has no valid active Scene.");
            if (string.IsNullOrEmpty(scene.path))
                throw new ProtocolException("INVALID_SCENE_STATE", "The Scene is untitled; use unity_save_scene_as with an Assets/*.unity path.");
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved) throw new ProtocolException("COMMAND_FAILED", "Unity did not save Scene '" + scene.path + "'.");
            return new Dictionary<string, object> { { "saved", true }, { "scene", SceneResolver.Serialize(scene) } };
        }

        private static object SaveSceneAs(IDictionary<string, object> parameters)
        {
            Scene scene = SceneResolver.ResolveLoaded(parameters, true);
            string destination = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "destinationPath"), ".unity", "destinationPath");
            bool overwrite = CommandArguments.OptionalBool(parameters, "overwrite", false);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(destination) != null && !overwrite)
                throw new ProtocolException("INVALID_ASSET_PATH", "A Scene already exists at '" + destination + "'. Set overwrite=true explicitly.");
            ProjectPathUtility.EnsureParentFolder(destination);
            bool saved = EditorSceneManager.SaveScene(scene, destination, false);
            if (!saved) throw new ProtocolException("COMMAND_FAILED", "Unity did not save Scene to '" + destination + "'.");
            AssetDatabase.Refresh();
            return new Dictionary<string, object>
            {
                { "saved", true }, { "overwrote", overwrite }, { "scene", SceneResolver.Serialize(scene) },
                { "undoSupported", false }
            };
        }

        private static object CloseScene(IDictionary<string, object> parameters)
        {
            Scene scene = SceneResolver.ResolveLoaded(parameters, false);
            if (scene.isDirty)
            {
                bool save = CommandArguments.OptionalBool(parameters, "saveModified", false);
                bool discard = CommandArguments.OptionalBool(parameters, "discardModified", false);
                if (save && discard) throw new ProtocolException("INVALID_ARGUMENT", "saveModified and discardModified are mutually exclusive.");
                if (!save && !discard) throw DirtySceneError(new List<Scene> { scene });
                if (save && !SaveSceneInternal(scene))
                    throw new ProtocolException("COMMAND_FAILED", "Unity did not save the modified Scene before closing it.");
            }
            bool remove = CommandArguments.OptionalBool(parameters, "removeScene", true);
            string path = scene.path;
            bool closed = EditorSceneManager.CloseScene(scene, remove);
            if (!closed) throw new ProtocolException("COMMAND_FAILED", "Unity did not close the Scene.");
            return new Dictionary<string, object> { { "closed", true }, { "scenePath", path }, { "removed", remove } };
        }

        private static object SetActiveScene(IDictionary<string, object> parameters)
        {
            Scene scene = SceneResolver.ResolveLoaded(parameters, false);
            if (!scene.isLoaded) throw new ProtocolException("INVALID_SCENE_STATE", "Only a loaded Scene can become active.");
            bool changed = SceneManager.SetActiveScene(scene);
            if (!changed) throw new ProtocolException("COMMAND_FAILED", "Unity did not set the active Scene.");
            return new Dictionary<string, object> { { "active", true }, { "scene", SceneResolver.Serialize(scene) } };
        }

        private static object EnterPlayMode()
        {
            if (EditorApplication.isPlaying)
                return PlayModeResult(false, "Unity is already in Play Mode.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return PlayModeResult(false, "Unity is already entering Play Mode.");
            EditorApplication.EnterPlaymode();
            return PlayModeResult(true, "Play Mode entry requested; use unity_wait_for_play_mode.");
        }

        private static object ExitPlayMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                return PlayModeResult(false, "Unity is already stopped.");
            EditorApplication.ExitPlaymode();
            return PlayModeResult(true, "Play Mode exit requested; use unity_wait_for_play_mode.");
        }

        private static object PausePlayMode(IDictionary<string, object> parameters)
        {
            if (!EditorApplication.isPlaying)
                throw new ProtocolException("INVALID_SCENE_STATE", "Unity must be in Play Mode before it can be paused.");
            EditorApplication.isPaused = CommandArguments.OptionalBool(parameters, "paused", true);
            return PlayModeResult(true, EditorApplication.isPaused ? "Play Mode paused." : "Play Mode resumed.");
        }

        private static object StepFrame()
        {
            if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
                throw new ProtocolException("INVALID_SCENE_STATE", "Unity must be playing and paused to step a frame.");
            EditorApplication.Step();
            return new Dictionary<string, object> { { "stepped", true }, { "isPlaying", true }, { "isPaused", true } };
        }

        private static object CaptureGameView(IDictionary<string, object> parameters)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
                foreach (Camera candidate in cameras)
                {
                    if (candidate.gameObject.scene.IsValid() && candidate.gameObject.activeInHierarchy)
                    { camera = candidate; break; }
                }
            }
            if (camera == null) throw new ProtocolException("CAPABILITY_UNAVAILABLE", "No active Camera is available for Game View capture.");
            return CaptureUtility.CaptureCamera(camera, CaptureWidth(parameters), CaptureHeight(parameters),
                CommandArguments.OptionalBool(parameters, "transparentBackground", false), "game-view");
        }

        private static object CaptureCamera(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Camera camera = gameObject.GetComponent<Camera>();
            if (camera == null) throw new ProtocolException("INVALID_ARGUMENT", "The referenced GameObject has no Camera component.");
            return CaptureUtility.CaptureCamera(camera, CaptureWidth(parameters), CaptureHeight(parameters),
                CommandArguments.OptionalBool(parameters, "transparentBackground", false), "camera");
        }

        private static object CaptureSceneView(IDictionary<string, object> parameters)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                throw new ProtocolException("CAPABILITY_UNAVAILABLE", "No active Scene View is available.");
            return CaptureUtility.CaptureCamera(view.camera, CaptureWidth(parameters), CaptureHeight(parameters),
                CommandArguments.OptionalBool(parameters, "transparentBackground", false), "scene-view");
        }

        private static object GetSelection()
        {
            List<object> values = new List<object>();
            foreach (UnityEngine.Object value in Selection.objects) values.Add(SerializeSelection(value));
            return new Dictionary<string, object>
            {
                { "count", values.Count }, { "objects", values },
                { "activeObject", Selection.activeObject == null ? null : SerializeSelection(Selection.activeObject) }
            };
        }

        private static object SetSelection(IDictionary<string, object> parameters)
        {
            IList<object> references = CommandArguments.RequiredArray(parameters, "objects");
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            foreach (object value in references)
            {
                IDictionary<string, object> reference = value as IDictionary<string, object>;
                if (reference == null) throw new ProtocolException("INVALID_ARGUMENT", "Each selection item must be an object reference.");
                objects.Add(CommandArguments.Has(reference, "assetPath") || CommandArguments.Has(reference, "guid")
                    ? AssetDatabase.LoadMainAssetAtPath(AssetResolver.ResolvePath(reference))
                    : GameObjectResolver.Resolve(reference));
            }
            Selection.objects = objects.ToArray();
            int activeIndex = CommandArguments.OptionalInt(parameters, "activeIndex", 0);
            if (objects.Count > 0)
            {
                if (activeIndex < 0 || activeIndex >= objects.Count)
                    throw new ProtocolException("INVALID_ARGUMENT", "activeIndex is outside the selection array.");
                Selection.activeObject = objects[activeIndex];
            }
            return GetSelection();
        }

        private static object FrameObject(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Selection.activeGameObject = gameObject;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) throw new ProtocolException("CAPABILITY_UNAVAILABLE", "No active Scene View is available.");
            view.FrameSelected();
            view.Repaint();
            return new Dictionary<string, object> { { "framed", true }, { "object", UnityObjectSerializer.BasicGameObject(gameObject) } };
        }

        private static int CaptureWidth(IDictionary<string, object> parameters)
        { return CommandArguments.OptionalInt(parameters, "width", 1280); }

        private static int CaptureHeight(IDictionary<string, object> parameters)
        { return CommandArguments.OptionalInt(parameters, "height", 720); }

        private static IDictionary<string, object> PlayModeResult(bool requested, string message)
        {
            return new Dictionary<string, object>
            {
                { "requested", requested }, { "message", message }, { "isPlaying", EditorApplication.isPlaying },
                { "isPaused", EditorApplication.isPaused }, { "isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode },
                { "transition", EditorStateTracker.PlayModeTransition }
            };
        }

        private static object SerializeSelection(UnityEngine.Object value)
        {
            GameObject gameObject = value as GameObject;
            if (gameObject != null && gameObject.scene.IsValid()) return UnityObjectSerializer.BasicGameObject(gameObject);
            Component component = value as Component;
            if (component != null && component.gameObject.scene.IsValid()) return UnityObjectSerializer.ComponentValue(component);
            return AssetResolver.Reference(value);
        }

        private static void HandleDirtyScenes(IDictionary<string, object> parameters)
        {
            List<Scene> dirty = new List<Scene>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && scene.isDirty) dirty.Add(scene);
            }
            if (dirty.Count == 0) return;
            bool save = CommandArguments.OptionalBool(parameters, "saveModified", false);
            bool discard = CommandArguments.OptionalBool(parameters, "discardModified", false);
            if (save && discard) throw new ProtocolException("INVALID_ARGUMENT", "saveModified and discardModified are mutually exclusive.");
            if (!save && !discard) throw DirtySceneError(dirty);
            if (save)
            {
                foreach (Scene scene in dirty)
                {
                    if (!SaveSceneInternal(scene))
                        throw new ProtocolException("COMMAND_FAILED", "Unity did not save all modified Scenes.");
                }
            }
        }

        private static bool SaveSceneInternal(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path))
                throw new ProtocolException("INVALID_SCENE_STATE", "Cannot save an untitled modified Scene non-interactively; save it with unity_save_scene_as first or set discardModified=true explicitly.");
            return EditorSceneManager.SaveScene(scene);
        }

        private static ProtocolException DirtySceneError(IList<Scene> scenes)
        {
            List<object> values = new List<object>();
            foreach (Scene scene in scenes) values.Add(SceneResolver.Serialize(scene));
            return new ProtocolException("INVALID_SCENE_STATE",
                "One or more Scenes have unsaved changes. Set saveModified=true or discardModified=true explicitly.",
                new Dictionary<string, object> { { "dirtyScenes", values } });
        }

        private static bool IsSceneOpen(string path)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (string.Equals(SceneManager.GetSceneAt(index).path, path, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsInBuildSettings(string path)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (string.Equals(scene.path, path, StringComparison.Ordinal)) return true;
            return false;
        }

        private static T ParseEnum<T>(string value, string argumentName) where T : struct
        {
            T result;
            if (!Enum.TryParse(value, false, out result))
                throw new ProtocolException("INVALID_ARGUMENT", "Unknown " + argumentName + " '" + value + "'.");
            return result;
        }
    }
}
