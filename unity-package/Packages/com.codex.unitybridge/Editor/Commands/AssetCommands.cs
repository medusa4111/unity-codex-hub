using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class AssetCommands
    {
        public static bool Handles(string command)
        {
            return command == "find_assets" || command == "get_asset_info"
                || command == "get_asset_dependencies" || command == "import_asset"
                || command == "get_asset_preview" || command == "ping_asset";
        }

        public static object Execute(string command, IDictionary<string, object> parameters)
        {
            switch (command)
            {
                case "find_assets": return FindAssets(parameters);
                case "get_asset_info": return GetAssetInfo(parameters);
                case "get_asset_dependencies": return GetDependencies(parameters);
                case "import_asset": return ImportAsset(parameters);
                case "get_asset_preview": return GetAssetPreview(parameters);
                case "ping_asset": return PingAsset(parameters);
                default: return null;
            }
        }

        private static object FindAssets(IDictionary<string, object> parameters)
        {
            string query = CommandArguments.OptionalString(parameters, "query", string.Empty);
            string type = CommandArguments.OptionalString(parameters, "type");
            if (type != null) query = (query + " t:" + type).Trim();
            IList<object> folderValues = CommandArguments.OptionalArray(parameters, "folders");
            string[] folders = null;
            if (folderValues.Count > 0)
            {
                folders = new string[folderValues.Count];
                for (int index = 0; index < folderValues.Count; index++)
                {
                    string folder = folderValues[index] as string;
                    if (folder == null || (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal)))
                        throw new ProtocolException("INVALID_ASSET_PATH", "Search folders must be under Assets.");
                    folders[index] = folder == "Assets" ? folder : ProjectPathUtility.NormalizeAssetPath(folder, "folders");
                }
            }
            int offset = CommandArguments.OptionalInt(parameters, "offset", 0);
            int limit = CommandArguments.OptionalInt(parameters, "limit", 100);
            string[] guids = folders == null ? AssetDatabase.FindAssets(query) : AssetDatabase.FindAssets(query, folders);
            List<object> results = new List<object>();
            int end = Math.Min(guids.Length, offset + limit);
            for (int index = Math.Min(offset, guids.Length); index < end; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                results.Add(new Dictionary<string, object>
                {
                    { "guid", guids[index] }, { "assetPath", path },
                    { "name", System.IO.Path.GetFileNameWithoutExtension(path) },
                    { "mainAssetType", mainType == null ? null : mainType.FullName },
                    { "category", mainType == null ? "Other" : AssetResolver.Category(mainType, path) }
                });
            }
            return new Dictionary<string, object>
            {
                { "results", results }, { "totalMatches", guids.Length }, { "returned", results.Count },
                { "offset", offset }, { "limit", limit }, { "truncated", end < guids.Length }
            };
        }

        private static object GetAssetInfo(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
            Type mainType = main.GetType();
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            List<object> subAssetValues = new List<object>();
            int subAssetCount = Math.Min(subAssets.Length, 100);
            for (int index = 0; index < subAssetCount; index++) subAssetValues.Add(AssetResolver.Reference(subAssets[index]));
            AssetImporter importer = AssetImporter.GetAtPath(path);
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "guid", AssetDatabase.AssetPathToGUID(path) }, { "assetPath", path }, { "name", main.name },
                { "mainAssetType", mainType.FullName }, { "category", AssetResolver.Category(mainType, path) },
                { "labels", AssetDatabase.GetLabels(main) }, { "subassets", subAssetValues },
                { "subassetsTruncated", subAssets.Length > subAssetCount },
                { "importerType", importer == null ? null : importer.GetType().FullName },
                { "isPrefab", PrefabUtility.GetPrefabAssetType(main) != PrefabAssetType.NotAPrefab },
                { "isScene", path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) }
            };
            Texture texture = main as Texture;
            if (texture != null) result["dimensions"] = new Dictionary<string, object> { { "width", texture.width }, { "height", texture.height } };
            AudioClip audio = main as AudioClip;
            if (audio != null) result["audio"] = new Dictionary<string, object> { { "length", audio.length }, { "channels", audio.channels }, { "frequency", audio.frequency } };
            Mesh mesh = main as Mesh;
            if (mesh != null) result["mesh"] = new Dictionary<string, object> { { "vertexCount", mesh.vertexCount }, { "subMeshCount", mesh.subMeshCount }, { "bounds", mesh.bounds.ToString() } };
            Material material = main as Material;
            if (material != null) result["shader"] = material.shader == null ? null : material.shader.name;
            if (CommandArguments.OptionalBool(parameters, "includeDependencies", false))
            {
                int limit = CommandArguments.OptionalInt(parameters, "dependencyLimit", 200);
                string[] dependencies = AssetDatabase.GetDependencies(path, true);
                result["dependencies"] = Slice(dependencies, 0, limit);
                result["dependenciesTruncated"] = dependencies.Length > limit;
            }
            return result;
        }

        private static object GetDependencies(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            bool recursive = CommandArguments.OptionalBool(parameters, "recursive", true);
            int offset = CommandArguments.OptionalInt(parameters, "offset", 0);
            int limit = CommandArguments.OptionalInt(parameters, "limit", 500);
            string[] dependencies = AssetDatabase.GetDependencies(path, recursive);
            return new Dictionary<string, object>
            {
                { "assetPath", path }, { "recursive", recursive },
                { "dependencies", Slice(dependencies, offset, limit) }, { "total", dependencies.Length },
                { "offset", offset }, { "limit", limit }, { "truncated", offset + limit < dependencies.Length }
            };
        }

        private static object ImportAsset(IDictionary<string, object> parameters)
        {
            string path = ProjectPathUtility.NormalizeAssetPath(CommandArguments.RequiredString(parameters, "assetPath"), "assetPath");
            ImportAssetOptions options = ImportAssetOptions.Default;
            if (CommandArguments.OptionalBool(parameters, "forceUpdate", false)) options |= ImportAssetOptions.ForceUpdate;
            if (CommandArguments.OptionalBool(parameters, "forceSynchronousImport", false)) options |= ImportAssetOptions.ForceSynchronousImport;
            AssetDatabase.ImportAsset(path, options);
            UnityEngine.Object imported = AssetDatabase.LoadMainAssetAtPath(path);
            if (imported == null) throw new ProtocolException("ASSET_NOT_FOUND", "Unity did not import an asset at '" + path + "'.");
            return new Dictionary<string, object>
            {
                { "imported", true }, { "asset", AssetResolver.Reference(imported) },
                { "isCompiling", EditorApplication.isCompiling }, { "isUpdating", EditorApplication.isUpdating }
            };
        }

        private static object GetAssetPreview(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            Texture2D preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null)
            {
                return new Dictionary<string, object>
                {
                    { "ready", false }, { "assetPath", path },
                    { "loading", IsPreviewLoading(asset) },
                    { "retryable", true }, { "message", "Unity preview is not ready; retry this tool." }
                };
            }
            return CaptureUtility.CaptureTexture(preview,
                CommandArguments.OptionalInt(parameters, "width", 256),
                CommandArguments.OptionalInt(parameters, "height", 256), "asset-preview");
        }

        private static object PingAsset(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            EditorGUIUtility.PingObject(asset);
            return new Dictionary<string, object> { { "pinged", true }, { "asset", AssetResolver.Reference(asset) } };
        }

        private static IList<object> Slice(string[] values, int offset, int limit)
        {
            List<object> result = new List<object>();
            int end = Math.Min(values.Length, offset + limit);
            for (int index = Math.Min(offset, values.Length); index < end; index++) result.Add(values[index]);
            return result;
        }

        private static bool IsPreviewLoading(UnityEngine.Object asset)
        {
#if UNITY_6000_5_OR_NEWER
            return AssetPreview.IsLoadingAssetPreview(asset.GetEntityId());
#else
            return AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID());
#endif
        }
    }
}
