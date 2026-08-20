using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Commands
{
    internal static class SceneResolver
    {
        public static Scene ResolveLoaded(IDictionary<string, object> parameters, bool allowActiveDefault)
        {
            string path = CommandArguments.OptionalString(parameters, "scenePath");
            string name = CommandArguments.OptionalString(parameters, "sceneName");
            if (path != null && name != null)
            {
                throw new ProtocolException("INVALID_ARGUMENT", "Provide only one of scenePath or sceneName.");
            }
            if (path == null && name == null)
            {
                if (allowActiveDefault) return SceneManager.GetActiveScene();
                throw new ProtocolException("INVALID_ARGUMENT", "Provide scenePath or sceneName.");
            }

            if (path != null) path = ProjectPathUtility.RequireExtension(path, ".unity", "scenePath");
            Scene match = default(Scene);
            int count = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                bool matches = path != null
                    ? string.Equals(scene.path, path, StringComparison.Ordinal)
                    : string.Equals(scene.name, name, StringComparison.Ordinal);
                if (!matches) continue;
                match = scene;
                count++;
            }
            if (count == 0)
            {
                throw new ProtocolException(
                    "SCENE_NOT_FOUND", "The requested Scene is not currently open.");
            }
            if (count > 1)
            {
                throw new ProtocolException(
                    "INVALID_ARGUMENT", "sceneName is ambiguous; use scenePath.");
            }
            return match;
        }

        public static IDictionary<string, object> Serialize(Scene scene)
        {
            return new Dictionary<string, object>
            {
                { "name", scene.name },
                { "path", scene.path },
                { "isLoaded", scene.isLoaded },
                { "isDirty", scene.isDirty },
                { "isActive", scene == SceneManager.GetActiveScene() },
                { "rootCount", scene.isLoaded ? scene.rootCount : 0 },
                { "buildIndex", scene.buildIndex }
            };
        }
    }
}
