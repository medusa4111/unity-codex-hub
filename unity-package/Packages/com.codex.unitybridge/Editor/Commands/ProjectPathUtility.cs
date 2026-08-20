using System;
using System.IO;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class ProjectPathUtility
    {
        public const string CaptureDirectoryRelative = "Library/UnityCodexBridge/Captures";

        public static string NormalizeAssetPath(string value, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw InvalidPath(argumentName, "must be a non-empty project-relative path.");
            }

            string normalized = value.Trim().Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || normalized.StartsWith("Assets//", StringComparison.Ordinal)
                || normalized.EndsWith("/", StringComparison.Ordinal))
            {
                throw InvalidPath(argumentName, "must be a normalized path below Assets/.");
            }

            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw InvalidPath(argumentName, "contains an invalid path segment.");
                }
            }

            string projectRoot = ProjectRoot;
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsRoot, StringComparison.Ordinal))
            {
                throw InvalidPath(argumentName, "escapes the project Assets directory.");
            }
            return normalized;
        }

        public static string RequireExtension(string assetPath, string extension, string argumentName)
        {
            string normalized = NormalizeAssetPath(assetPath, argumentName);
            if (!normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidPath(argumentName, "must end with " + extension + ".");
            }
            return normalized;
        }

        public static void EnsureParentFolder(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath, "assetPath");
            string directory = Path.GetDirectoryName(normalized).Replace('\\', '/');
            if (string.Equals(directory, "Assets", StringComparison.Ordinal)) return;

            string current = "Assets";
            string[] segments = directory.Substring("Assets/".Length).Split('/');
            foreach (string segment in segments)
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, segment);
                    if (string.IsNullOrEmpty(guid))
                    {
                        throw new ProtocolException(
                            "COMMAND_FAILED", "Unity could not create asset folder '" + next + "'.");
                    }
                }
                current = next;
            }
        }

        public static string CreateCapturePath(string prefix)
        {
            string directory = Path.Combine(ProjectRoot, CaptureDirectoryRelative);
            Directory.CreateDirectory(directory);
            string safePrefix = string.IsNullOrEmpty(prefix) ? "capture" : prefix;
            string fileName = safePrefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N") + ".png";
            return Path.Combine(directory, fileName);
        }

        public static string ToProjectRelativePath(string fullPath)
        {
            string root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalized = Path.GetFullPath(fullPath);
            if (!normalized.StartsWith(root, StringComparison.Ordinal))
            {
                throw new ProtocolException("INTERNAL_ERROR", "Generated file is outside the Unity project.");
            }
            return normalized.Substring(root.Length).Replace('\\', '/');
        }

        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        private static ProtocolException InvalidPath(string argumentName, string reason)
        {
            return new ProtocolException(
                "INVALID_ASSET_PATH",
                argumentName + " " + reason,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { "argument", argumentName },
                    { "requiredRoot", "Assets/" }
                });
        }
    }
}
