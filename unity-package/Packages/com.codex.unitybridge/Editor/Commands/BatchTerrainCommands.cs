using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class BatchTerrainCommands
    {
        private static readonly HashSet<string> BatchCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "create_game_object", "create_primitive", "set_transform", "set_game_object_properties", "delete_game_object"
        };

        public static bool Handles(string command)
        {
            return command == "batch" || command == "batch_instantiate_prefab"
                || command == "batch_set_transforms" || command == "scatter_prefab"
                || command == "create_terrain" || command == "get_terrain_info"
                || command == "set_terrain_heights" || command == "set_terrain_heights_patch"
                || command == "set_terrain_layers" || command == "set_terrain_alphamap_patch"
                || command == "add_terrain_trees";
        }

        public static object Execute(string command, IDictionary<string, object> parameters)
        {
            switch (command)
            {
                case "batch": return Batch(parameters);
                case "batch_instantiate_prefab": return BatchInstantiatePrefab(parameters);
                case "batch_set_transforms": return BatchSetTransforms(parameters);
                case "scatter_prefab": return ScatterPrefab(parameters);
                case "create_terrain": return CreateTerrain(parameters);
                case "get_terrain_info": return GetTerrainInfo(parameters);
                case "set_terrain_heights": return SetTerrainHeights(parameters, false);
                case "set_terrain_heights_patch": return SetTerrainHeights(parameters, true);
                case "set_terrain_layers": return SetTerrainLayers(parameters);
                case "set_terrain_alphamap_patch": return SetTerrainAlphamapPatch(parameters);
                case "add_terrain_trees": return AddTerrainTrees(parameters);
                default: return null;
            }
        }

        private static object Batch(IDictionary<string, object> parameters)
        {
            IList<object> operations = CommandArguments.RequiredArray(parameters, "operations");
            if (operations.Count < 1 || operations.Count > 100)
                throw new ProtocolException("INVALID_ARGUMENT", "operations must contain between 1 and 100 items.");
            bool stopOnError = CommandArguments.OptionalBool(parameters, "stopOnError", true);
            string undoName = CommandArguments.OptionalString(parameters, "undoGroupName", "Codex: Batch");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            List<object> results = new List<object>();
            int succeeded = 0;
            try
            {
                for (int index = 0; index < operations.Count; index++)
                {
                    IDictionary<string, object> operation = operations[index] as IDictionary<string, object>;
                    if (operation == null) throw new ProtocolException("INVALID_ARGUMENT", "Every batch operation must be an object.");
                    string command = CommandArguments.RequiredString(operation, "command");
                    if (!BatchCommands.Contains(command))
                        throw new ProtocolException("INVALID_ARGUMENT", "Command '" + command + "' is not allowed inside unity_batch.");
                    try
                    {
                        object result = GameObjectCommands.Execute(command, WithoutCommand(operation));
                        results.Add(BatchSuccess(index, command, result));
                        succeeded++;
                    }
                    catch (ProtocolException exception)
                    {
                        results.Add(BatchFailure(index, command, exception));
                        if (stopOnError) break;
                    }
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return new Dictionary<string, object>
            {
                { "succeeded", succeeded }, { "failed", results.Count - succeeded }, { "processed", results.Count },
                { "requested", operations.Count }, { "stoppedEarly", results.Count < operations.Count }, { "results", results },
                { "undoGroup", group }, { "undoGroupName", undoName }
            };
        }

        private static object BatchInstantiatePrefab(IDictionary<string, object> parameters)
        {
            IList<object> placements = CommandArguments.RequiredArray(parameters, "placements");
            if (placements.Count < 1 || placements.Count > 500)
                throw new ProtocolException("INVALID_ARGUMENT", "placements must contain between 1 and 500 items.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(CommandArguments.OptionalString(parameters, "undoGroupName", "Codex: Batch Instantiate Prefab"));
            List<object> created = new List<object>();
            try
            {
                foreach (object value in placements)
                {
                    IDictionary<string, object> placement = value as IDictionary<string, object>;
                    if (placement == null) throw new ProtocolException("INVALID_ARGUMENT", "Every placement must be an object.");
                    Dictionary<string, object> args = Copy(parameters, "placements", "undoGroupName");
                    foreach (KeyValuePair<string, object> pair in placement) args[pair.Key] = pair.Value;
                    IDictionary<string, object> result = PrefabMaterialCommands.InstantiatePrefab(args)
                        as IDictionary<string, object>;
                    created.Add(result == null ? null : ConciseObjectFromResult(result));
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return new Dictionary<string, object>
            {
                { "created", created.Count }, { "objects", created }, { "undoGroup", group }
            };
        }

        private static object BatchSetTransforms(IDictionary<string, object> parameters)
        {
            IList<object> items = CommandArguments.RequiredArray(parameters, "items");
            if (items.Count < 1 || items.Count > 1000)
                throw new ProtocolException("INVALID_ARGUMENT", "items must contain between 1 and 1000 entries.");
            bool stopOnError = CommandArguments.OptionalBool(parameters, "stopOnError", true);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(CommandArguments.OptionalString(parameters, "undoGroupName", "Codex: Batch Transform"));
            List<object> results = new List<object>();
            int succeeded = 0;
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    IDictionary<string, object> item = items[index] as IDictionary<string, object>;
                    try
                    {
                        if (item == null) throw new ProtocolException("INVALID_ARGUMENT", "Every transform item must be an object.");
                        GameObjectCommands.Execute("set_transform", item);
                        results.Add(BatchSuccess(index, "set_transform",
                            UnityObjectSerializer.BasicGameObject(GameObjectResolver.Resolve(item))));
                        succeeded++;
                    }
                    catch (ProtocolException exception)
                    {
                        results.Add(BatchFailure(index, "set_transform", exception));
                        if (stopOnError) break;
                    }
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return new Dictionary<string, object>
            {
                { "succeeded", succeeded }, { "failed", results.Count - succeeded }, { "processed", results.Count },
                { "requested", items.Count }, { "stoppedEarly", results.Count < items.Count }, { "results", results },
                { "undoGroup", group }
            };
        }

        private static object ScatterPrefab(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                throw new ProtocolException("PREFAB_NOT_FOUND", "Asset '" + path + "' is not a Prefab.");
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            int requested = CommandArguments.RequiredInt(parameters, "count");
            int seed = CommandArguments.RequiredInt(parameters, "seed");
            Vector3 center = CommandArguments.Vector3Value(CommandArguments.RequiredValue(parameters, "center", "scatter"), "center");
            bool hasSize = CommandArguments.Has(parameters, "size");
            bool hasRadius = CommandArguments.Has(parameters, "radius");
            if (hasSize == hasRadius) throw new ProtocolException("INVALID_ARGUMENT", "Provide exactly one of size or radius.");
            Vector3 size = hasSize ? CommandArguments.Vector3Value(CommandArguments.RequiredValue(parameters, "size", "scatter"), "size") : Vector3.zero;
            float radius = hasRadius ? (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(parameters, "radius", "scatter"), "radius") : 0f;
            float minScale = (float)CommandArguments.OptionalDouble(parameters, "minScale", 1);
            float maxScale = (float)CommandArguments.OptionalDouble(parameters, "maxScale", 1);
            if (maxScale < minScale) throw new ProtocolException("INVALID_ARGUMENT", "maxScale must be at least minScale.");
            bool randomYaw = CommandArguments.OptionalBool(parameters, "randomYaw", true);
            bool align = CommandArguments.OptionalBool(parameters, "alignToSurface", false);
            Vector3 direction = parameters.ContainsKey("raycastDirection")
                ? CommandArguments.Vector3Value(parameters["raycastDirection"], "raycastDirection") : Vector3.down;
            if (align && direction.sqrMagnitude < 0.000001f)
                throw new ProtocolException("INVALID_ARGUMENT", "raycastDirection cannot be zero.");
            int layerMask = CommandArguments.OptionalInt(parameters, "layerMask", -1);
            float minSpacing = (float)CommandArguments.OptionalDouble(parameters, "minSpacing", 0);
            int maxReturned = CommandArguments.OptionalInt(parameters, "maxReturnedObjects", 100);
            System.Random random = new System.Random(seed);
            List<Vector3> accepted = new List<Vector3>();
            List<object> returned = new List<object>();
            int attempts = 0;
            int maximumAttempts = Math.Max(requested * 50, requested);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Codex: Scatter Prefab");
            try
            {
                while (accepted.Count < requested && attempts++ < maximumAttempts)
                {
                    Vector3 position = hasSize ? RandomInBox(random, center, size) : RandomInDisk(random, center, radius);
                    Quaternion rotation = randomYaw ? Quaternion.Euler(0f, NextFloat(random, 0f, 360f), 0f) : Quaternion.identity;
                    if (align)
                    {
                        RaycastHit hit;
                        Vector3 normalized = direction.normalized;
                        Vector3 origin = position - normalized * 5000f;
                        if (!Physics.Raycast(origin, normalized, out hit, 10000f, layerMask)) continue;
                        position = hit.point;
                        rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * rotation;
                    }
                    if (!HasSpacing(accepted, position, minSpacing)) continue;
                    GameObject instance = parent == null
                        ? PrefabUtility.InstantiatePrefab(prefab) as GameObject
                        : PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
                    if (instance == null) throw new ProtocolException("COMMAND_FAILED", "Unity failed to instantiate the Prefab during scatter.");
                    Undo.RegisterCreatedObjectUndo(instance, "Codex: Scatter Prefab");
                    instance.transform.position = position;
                    instance.transform.rotation = rotation;
                    float scale = NextFloat(random, minScale, maxScale);
                    instance.transform.localScale = Vector3.one * scale;
                    GameObjectCommands.MarkSceneDirty(instance);
                    accepted.Add(position);
                    if (returned.Count < maxReturned) returned.Add(UnityObjectSerializer.BasicGameObject(instance));
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            return new Dictionary<string, object>
            {
                { "requested", requested }, { "created", accepted.Count }, { "seed", seed }, { "attempts", attempts },
                { "placementLimitReached", accepted.Count < requested }, { "objects", returned },
                { "objectsTruncated", accepted.Count > returned.Count }, { "undoGroup", group }
            };
        }

        private static object CreateTerrain(IDictionary<string, object> parameters)
        {
            string path = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "assetPath"), ".asset", "assetPath");
            bool overwrite = CommandArguments.OptionalBool(parameters, "overwrite", false);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                if (!overwrite) throw new ProtocolException("INVALID_ASSET_PATH", "An asset already exists at '" + path + "'.");
                AssetDatabase.DeleteAsset(path);
            }
            int heightmapResolution = CommandArguments.OptionalInt(parameters, "heightmapResolution", 513);
            if (!IsPowerOfTwo(heightmapResolution - 1))
                throw new ProtocolException("INVALID_ARGUMENT", "heightmapResolution must be a power of two plus one.");
            int alphamapResolution = CommandArguments.OptionalInt(parameters, "alphamapResolution", 512);
            if (!IsPowerOfTwo(alphamapResolution))
                throw new ProtocolException("INVALID_ARGUMENT", "alphamapResolution must be a power of two.");
            Vector3 size = CommandArguments.Vector3Value(CommandArguments.RequiredValue(parameters, "size", "terrain"), "size");
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new ProtocolException("INVALID_ARGUMENT", "Terrain size values must be positive.");
            ProjectPathUtility.EnsureParentFolder(path);
            TerrainData data = new TerrainData
            {
                heightmapResolution = heightmapResolution,
                alphamapResolution = alphamapResolution,
                size = size
            };
            AssetDatabase.CreateAsset(data, path);
            GameObject gameObject = Terrain.CreateTerrainGameObject(data);
            gameObject.name = CommandArguments.OptionalString(parameters, "name", "Terrain");
            gameObject.transform.position = CommandArguments.Has(parameters, "position")
                ? CommandArguments.Vector3Value(parameters["position"], "position") : Vector3.zero;
            Undo.RegisterCreatedObjectUndo(gameObject, "Codex: Create Terrain");
            AssetDatabase.SaveAssets();
            GameObjectCommands.MarkSceneDirty(gameObject);
            return new Dictionary<string, object>
            {
                { "created", true }, { "terrain", UnityObjectSerializer.DetailedGameObject(gameObject) },
                { "terrainData", AssetResolver.Reference(data) }, { "overwrote", overwrite }, { "undoSupportedForAsset", false }
            };
        }

        private static object GetTerrainInfo(IDictionary<string, object> parameters)
        {
            Terrain terrain = ResolveTerrain(parameters);
            TerrainData data = terrain.terrainData;
            List<object> layers = new List<object>();
            foreach (TerrainLayer layer in data.terrainLayers) layers.Add(AssetResolver.Reference(layer));
            List<object> prototypes = new List<object>();
            for (int index = 0; index < data.treePrototypes.Length; index++)
            {
                TreePrototype prototype = data.treePrototypes[index];
                prototypes.Add(new Dictionary<string, object>
                { { "index", index }, { "prefab", AssetResolver.Reference(prototype.prefab) }, { "bendFactor", prototype.bendFactor } });
            }
            return new Dictionary<string, object>
            {
                { "terrain", UnityObjectSerializer.BasicGameObject(terrain.gameObject) }, { "terrainData", AssetResolver.Reference(data) },
                { "size", Vector3Value(data.size) }, { "position", Vector3Value(terrain.transform.position) },
                { "heightmapResolution", data.heightmapResolution }, { "alphamapResolution", data.alphamapResolution },
                { "alphamapLayers", data.alphamapLayers }, { "terrainLayers", layers },
                { "treePrototypeCount", prototypes.Count }, { "treePrototypes", prototypes },
                { "treeInstanceCount", data.treeInstanceCount }, { "detailResolution", data.detailResolution }
            };
        }

        private static object SetTerrainHeights(IDictionary<string, object> parameters, bool patch)
        {
            Terrain terrain = ResolveTerrain(parameters);
            TerrainData data = terrain.terrainData;
            float[,] values = ToHeightArray(CommandArguments.RequiredArray(parameters, "heights"));
            int xBase = patch ? CommandArguments.RequiredInt(parameters, "xBase") : 0;
            int yBase = patch ? CommandArguments.RequiredInt(parameters, "yBase") : 0;
            if (!patch && (values.GetLength(0) != data.heightmapResolution || values.GetLength(1) != data.heightmapResolution))
                throw new ProtocolException("INVALID_ARGUMENT", "Full height data dimensions must match heightmapResolution.");
            if (xBase < 0 || yBase < 0 || xBase + values.GetLength(1) > data.heightmapResolution
                || yBase + values.GetLength(0) > data.heightmapResolution)
                throw new ProtocolException("INVALID_ARGUMENT", "Height patch exceeds Terrain bounds.");
            Undo.RecordObject(data, patch ? "Codex: Set Terrain Height Patch" : "Codex: Set Terrain Heights");
            data.SetHeights(xBase, yBase, values);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "updated", true }, { "terrainData", AssetResolver.Reference(data) }, { "xBase", xBase }, { "yBase", yBase },
                { "width", values.GetLength(1) }, { "height", values.GetLength(0) }
            };
        }

        private static object SetTerrainLayers(IDictionary<string, object> parameters)
        {
            TerrainData data = ResolveTerrain(parameters).terrainData;
            IList<object> values = CommandArguments.RequiredArray(parameters, "layers");
            TerrainLayer[] layers = new TerrainLayer[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                IDictionary<string, object> reference = values[index] as IDictionary<string, object>;
                if (reference == null) throw new ProtocolException("INVALID_ARGUMENT", "Each layer must be an asset reference.");
                layers[index] = AssetResolver.Load<TerrainLayer>(reference, "a TerrainLayer");
            }
            Undo.RecordObject(data, "Codex: Set Terrain Layers");
            data.terrainLayers = layers;
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            return new Dictionary<string, object> { { "updated", true }, { "terrainData", AssetResolver.Reference(data) }, { "layerCount", layers.Length } };
        }

        private static object SetTerrainAlphamapPatch(IDictionary<string, object> parameters)
        {
            TerrainData data = ResolveTerrain(parameters).terrainData;
            IList<object> rows = CommandArguments.RequiredArray(parameters, "values");
            if (rows.Count == 0) throw new ProtocolException("INVALID_ARGUMENT", "values cannot be empty.");
            IList<object> firstRow = rows[0] as IList<object>;
            if (firstRow == null || firstRow.Count == 0) throw new ProtocolException("INVALID_ARGUMENT", "values rows cannot be empty.");
            IList<object> firstCell = firstRow[0] as IList<object>;
            if (firstCell == null || firstCell.Count != data.alphamapLayers)
                throw new ProtocolException("INVALID_ARGUMENT", "Each alphamap cell must contain one weight per Terrain layer.");
            int height = rows.Count, width = firstRow.Count, layers = data.alphamapLayers;
            float[,,] map = new float[height, width, layers];
            for (int row = 0; row < height; row++)
            {
                IList<object> columns = rows[row] as IList<object>;
                if (columns == null || columns.Count != width) throw new ProtocolException("INVALID_ARGUMENT", "Alphamap rows must have equal width.");
                for (int column = 0; column < width; column++)
                {
                    IList<object> cell = columns[column] as IList<object>;
                    if (cell == null || cell.Count != layers) throw new ProtocolException("INVALID_ARGUMENT", "Alphamap cells must match Terrain layer count.");
                    float total = 0;
                    for (int layer = 0; layer < layers; layer++)
                    {
                        float weight = (float)CommandArguments.ToDouble(cell[layer], "values");
                        if (weight < 0 || weight > 1) throw new ProtocolException("INVALID_ARGUMENT", "Alphamap weights must be between 0 and 1.");
                        map[row, column, layer] = weight; total += weight;
                    }
                    if (total <= 0) throw new ProtocolException("INVALID_ARGUMENT", "Every alphamap cell must have a positive total weight.");
                    for (int layer = 0; layer < layers; layer++) map[row, column, layer] /= total;
                }
            }
            int x = CommandArguments.RequiredInt(parameters, "x"), y = CommandArguments.RequiredInt(parameters, "y");
            if (x < 0 || y < 0 || x + width > data.alphamapWidth || y + height > data.alphamapHeight)
                throw new ProtocolException("INVALID_ARGUMENT", "Alphamap patch exceeds Terrain bounds.");
            Undo.RecordObject(data, "Codex: Set Terrain Alphamap Patch");
            data.SetAlphamaps(x, y, map);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            { { "updated", true }, { "terrainData", AssetResolver.Reference(data) }, { "x", x }, { "y", y }, { "width", width }, { "height", height }, { "layers", layers } };
        }

        private static object AddTerrainTrees(IDictionary<string, object> parameters)
        {
            TerrainData data = ResolveTerrain(parameters).terrainData;
            IList<object> values = CommandArguments.RequiredArray(parameters, "trees");
            List<TreePrototype> prototypes = new List<TreePrototype>(data.treePrototypes);
            List<TreeInstance> instances = new List<TreeInstance>(data.treeInstances);
            foreach (object value in values)
            {
                IDictionary<string, object> tree = value as IDictionary<string, object>;
                if (tree == null) throw new ProtocolException("INVALID_ARGUMENT", "Each tree must be an object.");
                IDictionary<string, object> prefabReference = CommandArguments.RequiredObject(tree, "prefab");
                GameObject prefab = AssetResolver.Load<GameObject>(prefabReference, "a GameObject Prefab");
                if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                    throw new ProtocolException("PREFAB_NOT_FOUND", "Tree prefab reference is not a Prefab asset.");
                int prototypeIndex = FindOrAddPrototype(prototypes, prefab);
                Vector3 position = CommandArguments.Vector3Value(CommandArguments.RequiredValue(tree, "position", "tree"), "position");
                if (position.x < 0 || position.x > 1 || position.y < 0 || position.y > 1 || position.z < 0 || position.z > 1)
                    throw new ProtocolException("INVALID_ARGUMENT", "Tree position must be normalized to the 0..1 Terrain range.");
                TreeInstance instance = new TreeInstance
                {
                    prototypeIndex = prototypeIndex, position = position,
                    widthScale = (float)CommandArguments.OptionalDouble(tree, "widthScale", 1),
                    heightScale = (float)CommandArguments.OptionalDouble(tree, "heightScale", 1),
                    rotation = (float)CommandArguments.OptionalDouble(tree, "rotation", 0),
                    color = CommandArguments.Has(tree, "color") ? CommandArguments.ColorValue(tree["color"], "color") : Color.white,
                    lightmapColor = CommandArguments.Has(tree, "lightmapColor") ? CommandArguments.ColorValue(tree["lightmapColor"], "lightmapColor") : Color.white
                };
                instances.Add(instance);
            }
            Undo.RecordObject(data, "Codex: Add Terrain Trees");
            data.treePrototypes = prototypes.ToArray();
            data.SetTreeInstances(instances.ToArray(), true);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "added", values.Count }, { "treeInstanceCount", data.treeInstanceCount },
                { "treePrototypeCount", data.treePrototypes.Length }, { "terrainData", AssetResolver.Reference(data) }
            };
        }

        private static Terrain ResolveTerrain(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Terrain terrain = gameObject.GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null)
                throw new ProtocolException("INVALID_ARGUMENT", "The referenced GameObject has no Terrain with TerrainData.");
            return terrain;
        }

        private static float[,] ToHeightArray(IList<object> rows)
        {
            if (rows.Count == 0) throw new ProtocolException("INVALID_ARGUMENT", "heights cannot be empty.");
            IList<object> first = rows[0] as IList<object>;
            if (first == null || first.Count == 0) throw new ProtocolException("INVALID_ARGUMENT", "height rows cannot be empty.");
            float[,] result = new float[rows.Count, first.Count];
            for (int row = 0; row < rows.Count; row++)
            {
                IList<object> columns = rows[row] as IList<object>;
                if (columns == null || columns.Count != first.Count) throw new ProtocolException("INVALID_ARGUMENT", "height rows must have equal width.");
                for (int column = 0; column < columns.Count; column++)
                {
                    float height = (float)CommandArguments.ToDouble(columns[column], "heights");
                    if (height < 0 || height > 1) throw new ProtocolException("INVALID_ARGUMENT", "Height values must be between 0 and 1.");
                    result[row, column] = height;
                }
            }
            return result;
        }

        private static int FindOrAddPrototype(List<TreePrototype> prototypes, GameObject prefab)
        {
            for (int index = 0; index < prototypes.Count; index++) if (prototypes[index].prefab == prefab) return index;
            prototypes.Add(new TreePrototype { prefab = prefab });
            return prototypes.Count - 1;
        }

        private static IDictionary<string, object> BatchSuccess(int index, string command, object result)
        {
            return new Dictionary<string, object>
            { { "index", index }, { "command", command }, { "success", true }, { "result", result }, { "error", null } };
        }

        private static IDictionary<string, object> BatchFailure(int index, string command, ProtocolException exception)
        {
            Dictionary<string, object> error = new Dictionary<string, object>
            { { "code", exception.Code }, { "message", exception.Message } };
            if (exception.Details != null) error["details"] = exception.Details;
            return new Dictionary<string, object>
            { { "index", index }, { "command", command }, { "success", false }, { "result", null }, { "error", error } };
        }

        private static IDictionary<string, object> WithoutCommand(IDictionary<string, object> source)
        {
            Dictionary<string, object> copy = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in source) if (pair.Key != "command") copy[pair.Key] = pair.Value;
            return copy;
        }

        private static object ConciseObjectFromResult(IDictionary<string, object> result)
        {
            object instanceId;
            if (!result.TryGetValue("instanceId", out instanceId)) return result;
            Dictionary<string, object> reference = new Dictionary<string, object> { { "instanceId", instanceId } };
            return UnityObjectSerializer.BasicGameObject(GameObjectResolver.Resolve(reference));
        }

        private static Dictionary<string, object> Copy(IDictionary<string, object> source, params string[] excluded)
        {
            HashSet<string> skip = new HashSet<string>(excluded, StringComparer.Ordinal);
            Dictionary<string, object> copy = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in source) if (!skip.Contains(pair.Key)) copy[pair.Key] = pair.Value;
            return copy;
        }

        private static Vector3 RandomInBox(System.Random random, Vector3 center, Vector3 size)
        {
            return center + new Vector3(NextFloat(random, -size.x * .5f, size.x * .5f),
                NextFloat(random, -size.y * .5f, size.y * .5f), NextFloat(random, -size.z * .5f, size.z * .5f));
        }

        private static Vector3 RandomInDisk(System.Random random, Vector3 center, float radius)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            double distance = Math.Sqrt(random.NextDouble()) * radius;
            return center + new Vector3((float)(Math.Cos(angle) * distance), 0f, (float)(Math.Sin(angle) * distance));
        }

        private static bool HasSpacing(IList<Vector3> positions, Vector3 candidate, float spacing)
        {
            if (spacing <= 0) return true;
            float squared = spacing * spacing;
            foreach (Vector3 position in positions) if ((position - candidate).sqrMagnitude < squared) return false;
            return true;
        }

        private static float NextFloat(System.Random random, float minimum, float maximum)
        { return minimum + (float)random.NextDouble() * (maximum - minimum); }

        private static bool IsPowerOfTwo(int value)
        { return value > 0 && (value & (value - 1)) == 0; }

        private static IDictionary<string, object> Vector3Value(Vector3 value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "z", value.z } }; }
    }
}
