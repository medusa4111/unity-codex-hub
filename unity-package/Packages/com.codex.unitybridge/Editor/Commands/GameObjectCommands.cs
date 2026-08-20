using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Commands
{
    internal static class GameObjectCommands
    {
        public static bool Handles(string command)
        {
            return command == "create_game_object" || command == "create_primitive"
                || command == "duplicate_game_object" || command == "delete_game_object"
                || command == "reparent_game_object" || command == "set_game_object_properties"
                || command == "set_transform" || command == "add_component"
                || command == "remove_component" || command == "set_component_property"
                || command == "set_component_properties" || command == "resize_serialized_array"
                || command == "set_serialized_array_element";
        }

        public static object Execute(string command, IDictionary<string, object> parameters)
        {
            switch (command)
            {
                case "create_game_object": return CreateGameObject(parameters);
                case "create_primitive": return CreatePrimitive(parameters);
                case "duplicate_game_object": return DuplicateGameObject(parameters);
                case "delete_game_object": return DeleteGameObject(parameters);
                case "reparent_game_object": return ReparentGameObject(parameters);
                case "set_game_object_properties": return SetGameObjectProperties(parameters);
                case "set_transform": return SetTransform(parameters);
                case "add_component": return AddComponent(parameters);
                case "remove_component": return RemoveComponent(parameters);
                case "set_component_property": return SetComponentProperty(parameters);
                case "set_component_properties": return SetComponentProperties(parameters);
                case "resize_serialized_array": return ResizeSerializedArray(parameters);
                case "set_serialized_array_element": return SetSerializedArrayElement(parameters);
                default: return null;
            }
        }

        public static object CreateGameObject(IDictionary<string, object> parameters)
        {
            string name = CommandArguments.RequiredString(parameters, "name").Trim();
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            GameObject gameObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(gameObject, "Codex: Create " + name);
            if (parent != null) Undo.SetTransformParent(gameObject.transform, parent.transform, "Codex: Parent " + name);
            MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        public static object CreatePrimitive(IDictionary<string, object> parameters)
        {
            PrimitiveType primitiveType;
            string value = CommandArguments.RequiredString(parameters, "primitiveType");
            if (!Enum.TryParse(value, false, out primitiveType))
                throw new ProtocolException("INVALID_ARGUMENT", "Unknown primitiveType '" + value + "'.");
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = CommandArguments.OptionalString(parameters, "name", value);
            Undo.RegisterCreatedObjectUndo(gameObject, "Codex: Create " + value);
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            if (parent != null) Undo.SetTransformParent(gameObject.transform, parent.transform, "Codex: Parent Primitive");
            ApplyTransformValues(gameObject, parameters, "local");
            MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        public static object DuplicateGameObject(IDictionary<string, object> parameters)
        {
            GameObject source = GameObjectResolver.Resolve(parameters);
            GameObject duplicate = UnityEngine.Object.Instantiate(source);
            duplicate.name = CommandArguments.OptionalString(parameters, "newName", source.name);
            Undo.RegisterCreatedObjectUndo(duplicate, "Codex: Duplicate " + source.name);
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            bool worldPositionStays = CommandArguments.OptionalBool(parameters, "worldPositionStays", true);
            Transform targetParent = parent == null ? source.transform.parent : parent.transform;
            duplicate.transform.SetParent(targetParent, worldPositionStays);
            ApplyTransformValues(duplicate, parameters, "local");
            MarkSceneDirty(duplicate);
            return UnityObjectSerializer.DetailedGameObject(duplicate);
        }

        public static object DeleteGameObject(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            string name = gameObject.name;
            string path = GameObjectResolver.HierarchyPath(gameObject);
            object instanceId = UnityObjectId.Get(gameObject);
            Scene scene = gameObject.scene;
            Undo.DestroyObjectImmediate(gameObject);
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return new Dictionary<string, object>
            { { "name", name }, { "instanceId", instanceId }, { "hierarchyPath", path }, { "deleted", true } };
        }

        public static object ReparentGameObject(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            GameObject parent = GameObjectResolver.ResolveOptionalParent(parameters);
            if (parent == gameObject || (parent != null && parent.transform.IsChildOf(gameObject.transform)))
                throw new ProtocolException("INVALID_ARGUMENT", "Reparenting would create an illegal hierarchy cycle.");
            bool worldPositionStays = CommandArguments.OptionalBool(parameters, "worldPositionStays", true);
            Vector3 localPosition = gameObject.transform.localPosition;
            Quaternion localRotation = gameObject.transform.localRotation;
            Vector3 localScale = gameObject.transform.localScale;
            Undo.SetTransformParent(gameObject.transform, parent == null ? null : parent.transform, "Codex: Reparent " + gameObject.name);
            if (!worldPositionStays)
            {
                gameObject.transform.localPosition = localPosition;
                gameObject.transform.localRotation = localRotation;
                gameObject.transform.localScale = localScale;
            }
            MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        public static object SetGameObjectProperties(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Undo.RecordObject(gameObject, "Codex: Set GameObject Properties");
            string name = CommandArguments.OptionalString(parameters, "name");
            if (name != null) gameObject.name = name;
            object value;
            if (parameters.TryGetValue("active", out value) && value != null) gameObject.SetActive((bool)value);
            string tag = CommandArguments.OptionalString(parameters, "tag");
            if (tag != null)
            {
                try { gameObject.tag = tag; }
                catch (UnityException) { throw new ProtocolException("INVALID_ARGUMENT", "Tag '" + tag + "' is not defined."); }
            }
            if (parameters.TryGetValue("layer", out value) && value != null)
            {
                string layerName = value as string;
                int layer = layerName == null ? CommandArguments.ToInt(value, "layer") : LayerMask.NameToLayer(layerName);
                if (layer < 0 || layer > 31) throw new ProtocolException("INVALID_ARGUMENT", "Layer is not defined.");
                gameObject.layer = layer;
            }
            if (parameters.TryGetValue("staticFlags", out value) && value != null)
            {
                int flags = CommandArguments.ToInt(value, "staticFlags");
                GameObjectUtility.SetStaticEditorFlags(gameObject, (StaticEditorFlags)flags);
            }
            EditorUtility.SetDirty(gameObject);
            MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        public static object SetTransform(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            string space = CommandArguments.OptionalString(parameters, "space", "local");
            if (space != "local" && space != "world") throw new ProtocolException("INVALID_ARGUMENT", "space must be local or world.");
            if (space == "world" && CommandArguments.Has(parameters, "scale"))
                throw new ProtocolException("INVALID_ARGUMENT", "World-space scale is not supported.");
            if (!CommandArguments.Has(parameters, "position") && !CommandArguments.Has(parameters, "rotation") && !CommandArguments.Has(parameters, "scale"))
                throw new ProtocolException("INVALID_ARGUMENT", "Provide position, rotation, or scale.");
            Undo.RecordObject(gameObject.transform, "Codex: Set Transform");
            ApplyTransformValues(gameObject, parameters, space);
            EditorUtility.SetDirty(gameObject.transform);
            MarkSceneDirty(gameObject);
            return UnityObjectSerializer.DetailedGameObject(gameObject);
        }

        public static object AddComponent(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Type componentType = ComponentResolver.ResolveType(CommandArguments.RequiredString(parameters, "componentType"));
            Component component;
            try { component = Undo.AddComponent(gameObject, componentType); }
            catch (Exception exception) { throw new ProtocolException("COMMAND_FAILED", "Unity could not add component: " + exception.Message); }
            if (component == null) throw new ProtocolException("COMMAND_FAILED", "Unity returned no component after adding it.");
            MarkSceneDirty(gameObject);
            return new Dictionary<string, object>
            { { "gameObject", UnityObjectSerializer.BasicGameObject(gameObject) }, { "component", UnityObjectSerializer.ComponentValue(component) } };
        }

        public static object RemoveComponent(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            if (component is Transform) throw new ProtocolException("INVALID_ARGUMENT", "Transform cannot be removed.");
            IDictionary<string, object> oldValue = UnityObjectSerializer.ComponentValue(component);
            Undo.DestroyObjectImmediate(component);
            MarkSceneDirty(gameObject);
            return new Dictionary<string, object> { { "removed", true }, { "component", oldValue }, { "gameObject", UnityObjectSerializer.BasicGameObject(gameObject) } };
        }

        private static object SetComponentProperty(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            string path = CommandArguments.RequiredString(parameters, "propertyPath");
            object value;
            if (!parameters.TryGetValue("value", out value)) throw new ProtocolException("INVALID_ARGUMENT", "value is required.");
            object result = SerializedPropertyWriter.Set(component, path, value);
            MarkSceneDirty(gameObject); return result;
        }

        private static object SetComponentProperties(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            object result = SerializedPropertyWriter.SetMany(component,
                CommandArguments.RequiredArray(parameters, "properties"), "Codex: Set Component Properties");
            MarkSceneDirty(gameObject); return result;
        }

        private static object ResizeSerializedArray(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            object result = SerializedPropertyWriter.ResizeArray(component,
                CommandArguments.RequiredString(parameters, "propertyPath"), CommandArguments.RequiredInt(parameters, "size"));
            MarkSceneDirty(gameObject); return result;
        }

        private static object SetSerializedArrayElement(IDictionary<string, object> parameters)
        {
            GameObject gameObject = GameObjectResolver.Resolve(parameters);
            Component component = ComponentResolver.ResolveComponent(gameObject, parameters);
            object value;
            if (!parameters.TryGetValue("value", out value)) throw new ProtocolException("INVALID_ARGUMENT", "value is required.");
            object result = SerializedPropertyWriter.SetArrayElement(component,
                CommandArguments.RequiredString(parameters, "propertyPath"), CommandArguments.RequiredInt(parameters, "index"), value);
            MarkSceneDirty(gameObject); return result;
        }

        public static void ApplyTransformValues(GameObject gameObject, IDictionary<string, object> parameters, string space)
        {
            object value;
            if (parameters.TryGetValue("position", out value) && value != null)
            {
                Vector3 position = CommandArguments.Vector3Value(value, "position");
                if (space == "world") gameObject.transform.position = position; else gameObject.transform.localPosition = position;
            }
            if (parameters.TryGetValue("rotation", out value) && value != null)
            {
                Quaternion rotation = Quaternion.Euler(CommandArguments.Vector3Value(value, "rotation"));
                if (space == "world") gameObject.transform.rotation = rotation; else gameObject.transform.localRotation = rotation;
            }
            if (parameters.TryGetValue("scale", out value) && value != null)
                gameObject.transform.localScale = CommandArguments.Vector3Value(value, "scale");
        }

        public static void MarkSceneDirty(GameObject gameObject)
        {
            if (gameObject != null && gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
}
