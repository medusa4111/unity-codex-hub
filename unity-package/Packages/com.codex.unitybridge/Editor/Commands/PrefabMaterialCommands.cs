using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Codex.UnityBridge.Commands
{
    internal static class PrefabMaterialCommands
    {
        public static bool Handles(string command)
        {
            return command == "instantiate_prefab" || command == "get_prefab_info"
                || command == "save_game_object_as_prefab" || command == "apply_prefab_instance"
                || command == "revert_prefab_instance" || command == "create_material"
                || command == "get_material_properties" || command == "set_material_property"
                || command == "create_scriptable_object";
        }

        public static object Execute(string command, IDictionary<string, object> parameters)
        {
            switch (command)
            {
                case "instantiate_prefab": return InstantiatePrefab(parameters);
                case "get_prefab_info": return GetPrefabInfo(parameters);
                case "save_game_object_as_prefab": return SaveAsPrefab(parameters);
                case "apply_prefab_instance": return ApplyPrefab(parameters);
                case "revert_prefab_instance": return RevertPrefab(parameters);
                case "create_material": return CreateMaterial(parameters);
                case "get_material_properties": return GetMaterialProperties(parameters);
                case "set_material_property": return SetMaterialProperty(parameters);
                case "create_scriptable_object": return CreateScriptableObject(parameters);
                default: return null;
            }
        }

        public static object InstantiatePrefab(IDictionary<string, object> parameters)
        {
            string path = AssetResolver.ResolvePath(parameters);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                throw new ProtocolException("PREFAB_NOT_FOUND", "Asset '" + path + "' is not a Prefab.");
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            UnityEngine.Object created = parent == null
                ? PrefabUtility.InstantiatePrefab(prefab)
                : PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            GameObject gameObject = created as GameObject;
            if (gameObject == null) throw new ProtocolException("COMMAND_FAILED", "Unity failed to instantiate the Prefab.");
            string name = CommandArguments.OptionalString(parameters, "name");
            if (name != null) gameObject.name = name;
            Undo.RegisterCreatedObjectUndo(gameObject, "Codex: Instantiate Prefab");
            GameObjectCommands.ApplyTransformValues(gameObject, parameters, "local");
            GameObjectCommands.MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        private static object GetPrefabInfo(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(gameObject);
            return new Dictionary<string, object>
            {
                { "object", UnityObjectSerializer.BasicGameObject(gameObject) },
                { "assetType", PrefabUtility.GetPrefabAssetType(gameObject).ToString() },
                { "instanceStatus", PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString() },
                { "isPartOfPrefabInstance", PrefabUtility.IsPartOfPrefabInstance(gameObject) },
                { "isOutermostRoot", PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject) },
                { "instanceRoot", root == null ? null : UnityObjectSerializer.BasicGameObject(root) },
                { "prefabAssetPath", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) },
                { "sourceObject", source == null ? null : AssetResolver.Reference(source) },
                { "propertyModificationCount", modifications == null ? 0 : modifications.Length },
                { "hasOverrides", PrefabUtility.HasPrefabInstanceAnyOverrides(gameObject, false) }
            };
        }

        private static object SaveAsPrefab(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            string path = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "assetPath"), ".prefab", "assetPath");
            bool overwrite = CommandArguments.OptionalBool(parameters, "overwrite", false);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && !overwrite)
                throw new ProtocolException("INVALID_ASSET_PATH", "A Prefab already exists at '" + path + "'. Set overwrite=true explicitly.");
            ProjectPathUtility.EnsureParentFolder(path);
            bool success;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(gameObject, path, out success);
            if (!success || asset == null) throw new ProtocolException("COMMAND_FAILED", "Unity failed to save the Prefab.");
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "saved", true }, { "overwrote", overwrite }, { "asset", AssetResolver.Reference(asset) },
                { "undoSupported", false }
            };
        }

        private static object ApplyPrefab(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);
            if (root == null) throw new ProtocolException("INVALID_ARGUMENT", "GameObject is not part of a Prefab instance.");
            PropertyModification[] before = PrefabUtility.GetPropertyModifications(root);
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            IList<object> changes = PrefabChanges(before);
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            return new Dictionary<string, object>
            {
                { "applied", true }, { "prefabAssetPath", path },
                { "appliedPropertyModificationCount", before == null ? 0 : before.Length },
                { "changes", changes }, { "changesTruncated", before != null && before.Length > changes.Count },
                { "undoSupported", true }
            };
        }

        private static object RevertPrefab(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);
            if (root == null) throw new ProtocolException("INVALID_ARGUMENT", "GameObject is not part of a Prefab instance.");
            PropertyModification[] before = PrefabUtility.GetPropertyModifications(root);
            IList<object> changes = PrefabChanges(before);
            PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
            return new Dictionary<string, object>
            {
                { "reverted", true }, { "revertedPropertyModificationCount", before == null ? 0 : before.Length },
                { "changes", changes }, { "changesTruncated", before != null && before.Length > changes.Count },
                { "object", UnityObjectSerializer.BasicGameObject(root) }, { "undoSupported", true }
            };
        }

        private static object CreateMaterial(IDictionary<string, object> parameters)
        {
            string path = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "assetPath"), ".mat", "assetPath");
            bool overwrite = CommandArguments.OptionalBool(parameters, "overwrite", false);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                if (!overwrite) throw new ProtocolException("INVALID_ASSET_PATH", "An asset already exists at '" + path + "'.");
                AssetDatabase.DeleteAsset(path);
            }
            Shader shader = Shader.Find(CommandArguments.RequiredString(parameters, "shaderName"));
            if (shader == null) throw new ProtocolException("ASSET_NOT_FOUND", "Shader was not found.");
            ProjectPathUtility.EnsureParentFolder(path);
            Material material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "created", true }, { "overwrote", overwrite }, { "material", AssetResolver.Reference(material) },
                { "shader", shader.name }, { "undoSupported", false }
            };
        }

        private static object GetMaterialProperties(IDictionary<string, object> parameters)
        {
            Material material = AssetResolver.Load<Material>(parameters, "a Material");
            List<object> properties = new List<object>();
            Shader shader = material.shader;
            int count = shader == null ? 0 : shader.GetPropertyCount();
            for (int index = 0; index < count; index++)
            {
                string name = shader.GetPropertyName(index);
                ShaderPropertyType type = shader.GetPropertyType(index);
                properties.Add(new Dictionary<string, object>
                {
                    { "name", name }, { "description", shader.GetPropertyDescription(index) },
                    { "type", type.ToString() }, { "value", MaterialValue(material, name, type) }
                });
            }
            return new Dictionary<string, object>
            {
                { "material", AssetResolver.Reference(material) }, { "shader", shader == null ? null : shader.name },
                { "keywords", material.shaderKeywords }, { "properties", properties }, { "count", properties.Count }
            };
        }

        private static object SetMaterialProperty(IDictionary<string, object> parameters)
        {
            Material material = AssetResolver.Load<Material>(parameters, "a Material");
            string propertyName = CommandArguments.RequiredString(parameters, "propertyName");
            if (!material.HasProperty(propertyName)) throw new ProtocolException("PROPERTY_NOT_FOUND", "Material property was not found.");
            object value;
            if (!parameters.TryGetValue("value", out value)) throw new ProtocolException("INVALID_ARGUMENT", "value is required.");
            Shader shader = material.shader;
            int index = shader.FindPropertyIndex(propertyName);
            ShaderPropertyType type = shader.GetPropertyType(index);
            Undo.RecordObject(material, "Codex: Set Material Property");
            switch (type)
            {
                case ShaderPropertyType.Color: material.SetColor(propertyName, CommandArguments.ColorValue(value, propertyName)); break;
                case ShaderPropertyType.Vector: material.SetVector(propertyName, CommandArguments.Vector4Value(value, propertyName)); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: material.SetFloat(propertyName, (float)CommandArguments.ToDouble(value, propertyName)); break;
                case ShaderPropertyType.Texture:
                    IDictionary<string, object> reference = value as IDictionary<string, object>;
                    material.SetTexture(propertyName, value == null ? null : AssetResolver.Load<Texture>(reference, "a Texture")); break;
#if UNITY_2021_2_OR_NEWER
                case ShaderPropertyType.Int: material.SetInteger(propertyName, CommandArguments.ToInt(value, propertyName)); break;
#endif
                default: throw new ProtocolException("CAPABILITY_UNAVAILABLE", "Material property type is not writable.");
            }
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "material", AssetResolver.Reference(material) }, { "propertyName", propertyName },
                { "propertyType", type.ToString() }, { "value", MaterialValue(material, propertyName, type) }
            };
        }

        private static object CreateScriptableObject(IDictionary<string, object> parameters)
        {
            string path = ProjectPathUtility.RequireExtension(
                CommandArguments.RequiredString(parameters, "assetPath"), ".asset", "assetPath");
            bool overwrite = CommandArguments.OptionalBool(parameters, "overwrite", false);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                if (!overwrite) throw new ProtocolException("INVALID_ASSET_PATH", "An asset already exists at '" + path + "'.");
                AssetDatabase.DeleteAsset(path);
            }
            Type type = UnityTypeResolver.ResolveScriptableObject(CommandArguments.RequiredString(parameters, "type"));
            ScriptableObject asset = ScriptableObject.CreateInstance(type);
            if (asset == null) throw new ProtocolException("COMMAND_FAILED", "Unity could not construct the ScriptableObject.");
            ProjectPathUtility.EnsureParentFolder(path);
            AssetDatabase.CreateAsset(asset, path);
            IList<object> initial = CommandArguments.OptionalArray(parameters, "initialProperties");
            if (initial.Count > 0) SerializedPropertyWriter.SetMany(asset, initial, "Codex: Initialize ScriptableObject");
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "created", true }, { "asset", AssetResolver.Reference(asset) },
                { "initializedPropertyCount", initial.Count }, { "undoSupported", false }
            };
        }

        private static object MaterialValue(Material material, string name, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                    Color c = material.GetColor(name); return new Dictionary<string, object> { { "r", c.r }, { "g", c.g }, { "b", c.b }, { "a", c.a } };
                case ShaderPropertyType.Vector:
                    Vector4 v = material.GetVector(name); return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z }, { "w", v.w } };
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return material.GetFloat(name);
                case ShaderPropertyType.Texture: return AssetResolver.Reference(material.GetTexture(name));
#if UNITY_2021_2_OR_NEWER
                case ShaderPropertyType.Int: return material.GetInteger(name);
#endif
                default: return null;
            }
        }

        private static IList<object> PrefabChanges(PropertyModification[] modifications)
        {
            List<object> changes = new List<object>();
            if (modifications == null) return changes;
            int count = Math.Min(modifications.Length, 200);
            for (int index = 0; index < count; index++)
            {
                PropertyModification modification = modifications[index];
                changes.Add(new Dictionary<string, object>
                {
                    { "propertyPath", modification.propertyPath },
                    { "target", modification.target == null ? null : AssetResolver.Reference(modification.target) },
                    { "value", modification.value },
                    { "objectReference", modification.objectReference == null ? null : AssetResolver.Reference(modification.objectReference) }
                });
            }
            return changes;
        }
    }
}
