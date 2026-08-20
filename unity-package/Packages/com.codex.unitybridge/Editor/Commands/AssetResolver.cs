using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class AssetResolver
    {
        public static string ResolvePath(IDictionary<string, object> parameters)
        {
            string assetPath = CommandArguments.OptionalString(parameters, "assetPath");
            string guid = CommandArguments.OptionalString(parameters, "guid");
            if ((assetPath == null) == (guid == null))
            {
                throw new ProtocolException(
                    "INVALID_ARGUMENT", "Provide exactly one of assetPath or guid.");
            }

            if (guid != null)
            {
                if (guid.Length != 32 || !IsHex(guid))
                {
                    throw new ProtocolException("INVALID_ARGUMENT", "guid must contain 32 hexadecimal characters.");
                }
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    throw new ProtocolException("ASSET_NOT_FOUND", "No asset exists for GUID '" + guid + "'.");
                }
            }

            assetPath = ProjectPathUtility.NormalizeAssetPath(assetPath, "assetPath");
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
            {
                throw new ProtocolException("ASSET_NOT_FOUND", "Asset '" + assetPath + "' was not found.");
            }
            return assetPath;
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'))) return false;
            return true;
        }

        public static T Load<T>(IDictionary<string, object> parameters, string expectedDescription)
            where T : UnityEngine.Object
        {
            string path = ResolvePath(parameters);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                UnityEngine.Object actual = AssetDatabase.LoadMainAssetAtPath(path);
                throw new ProtocolException(
                    "INVALID_ARGUMENT",
                    "Asset '" + path + "' is not " + expectedDescription + ".",
                    new Dictionary<string, object>
                    {
                        { "expectedType", typeof(T).FullName },
                        { "actualType", actual == null ? null : actual.GetType().FullName }
                    });
            }
            return asset;
        }

        public static IDictionary<string, object> Reference(UnityEngine.Object asset)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "name", asset.name },
                { "type", asset.GetType().FullName },
                { "instanceId", UnityObjectId.Get(asset) }
            };
            if (!string.IsNullOrEmpty(path))
            {
                result["assetPath"] = path;
                result["guid"] = AssetDatabase.AssetPathToGUID(path);
            }
            return result;
        }

        public static string Category(Type type, string path)
        {
            if (typeof(GameObject).IsAssignableFrom(type) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return "Prefab";
            if (typeof(Material).IsAssignableFrom(type)) return "Material";
            if (typeof(Texture).IsAssignableFrom(type)) return "Texture";
            if (typeof(Sprite).IsAssignableFrom(type)) return "Sprite";
            if (typeof(Mesh).IsAssignableFrom(type)) return "Mesh";
            if (typeof(AudioClip).IsAssignableFrom(type)) return "AudioClip";
            if (typeof(AnimationClip).IsAssignableFrom(type)) return "AnimationClip";
            if (typeof(ScriptableObject).IsAssignableFrom(type)) return "ScriptableObject";
            if (typeof(Shader).IsAssignableFrom(type)) return "Shader";
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return "Scene";
            return "Other";
        }
    }
}
