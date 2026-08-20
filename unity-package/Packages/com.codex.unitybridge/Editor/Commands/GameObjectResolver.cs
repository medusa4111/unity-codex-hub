using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Commands
{
    internal static class GameObjectResolver
    {
        public static GameObject Resolve(IDictionary<string, object> parameters)
        {
            return Resolve(parameters, "instanceId", "hierarchyPath", false);
        }

        public static GameObject ResolveOptionalParent(IDictionary<string, object> parameters)
        {
            return Resolve(parameters, "parentInstanceId", "parentPath", true);
        }

        public static string HierarchyPath(GameObject gameObject)
        {
            List<string> segments = new List<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                segments.Add(EscapeSegment(current.name));
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private static GameObject Resolve(
            IDictionary<string, object> parameters,
            string instanceIdName,
            string pathName,
            bool optional)
        {
            string instanceId;
            bool hasInstanceId = CommandArguments.TryObjectId(parameters, instanceIdName, out instanceId);
            string hierarchyPath = CommandArguments.OptionalString(parameters, pathName);

            if (hasInstanceId && hierarchyPath != null)
            {
                throw new ProtocolException(
                    "INVALID_ARGUMENT", "Provide only one of " + instanceIdName + " or " + pathName + ".");
            }
            if (!hasInstanceId && hierarchyPath == null)
            {
                if (optional) return null;
                throw new ProtocolException(
                    "INVALID_ARGUMENT", "Provide " + instanceIdName + " or " + pathName + ".");
            }

            GameObject result = hasInstanceId
                ? UnityObjectId.Resolve(instanceId) as GameObject
                : ResolvePath(hierarchyPath);
            if (result == null || !result.scene.IsValid() || !result.scene.isLoaded
                || (!hasInstanceId && result.scene != SceneManager.GetActiveScene()))
            {
                throw new ProtocolException(
                    "OBJECT_NOT_FOUND",
                    hasInstanceId
                        ? "No loaded-Scene GameObject with instanceId " + instanceId + " exists."
                        : "GameObject '" + hierarchyPath + "' was not found in the active scene.");
            }
            return result;
        }

        private static GameObject ResolvePath(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return null;
            }

            string[] rawSegments = hierarchyPath.Split(new[] { '/' }, StringSplitOptions.None);
            string[] segments = new string[rawSegments.Length];
            for (int index = 0; index < rawSegments.Length; index++)
            {
                segments[index] = UnescapeSegment(rawSegments[index]);
            }

            GameObject current = FindUniqueRoot(segments[0]);
            for (int index = 1; index < segments.Length && current != null; index++)
            {
                current = FindUniqueChild(current.transform, segments[index]);
            }
            return current;
        }

        private static GameObject FindUniqueRoot(string name)
        {
            GameObject match = null;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (!string.Equals(root.name, name, StringComparison.Ordinal)) continue;
                if (match != null)
                {
                    throw new ProtocolException(
                        "INVALID_ARGUMENT",
                        "Hierarchy path is ambiguous because multiple root objects are named '" + name
                        + "'. Use instanceId instead.");
                }
                match = root;
            }
            return match;
        }

        private static GameObject FindUniqueChild(Transform parent, string name)
        {
            GameObject match = null;
            for (int index = 0; index < parent.childCount; index++)
            {
                GameObject child = parent.GetChild(index).gameObject;
                if (!string.Equals(child.name, name, StringComparison.Ordinal)) continue;
                if (match != null)
                {
                    throw new ProtocolException(
                        "INVALID_ARGUMENT",
                        "Hierarchy path is ambiguous below '" + HierarchyPath(parent.gameObject)
                        + "'. Use instanceId instead.");
                }
                match = child;
            }
            return match;
        }

        private static string EscapeSegment(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }

        private static string UnescapeSegment(string segment)
        {
            return segment.Replace("~1", "/").Replace("~0", "~");
        }
    }
}
