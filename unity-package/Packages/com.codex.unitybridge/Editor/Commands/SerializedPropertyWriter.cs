using System;
using System.Collections.Generic;
using System.Text;
using Codex.UnityBridge.Protocol;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class SerializedPropertyWriter
    {
        public static IDictionary<string, object> Set(UnityEngine.Object target, string requestedPath, object value)
        {
            Undo.RecordObject(target, "Codex: Set " + requestedPath);
            SerializedObject serializedObject = Prepare(target);
            SerializedProperty property = RequireProperty(serializedObject, requestedPath, target);
            AssignValue(property, value);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return Result(target, new[] { property.propertyPath });
        }

        public static IDictionary<string, object> SetMany(
            UnityEngine.Object target,
            IList<object> changes,
            string undoName)
        {
            if (changes.Count == 0 || changes.Count > 128)
                throw new ProtocolException("INVALID_ARGUMENT", "properties must contain 1 to 128 changes.");
            Undo.RecordObject(target, undoName);
            SerializedObject serializedObject = Prepare(target);
            List<string> changed = new List<string>();
            foreach (object item in changes)
            {
                IDictionary<string, object> change = item as IDictionary<string, object>;
                if (change == null) throw new ProtocolException("INVALID_ARGUMENT", "Each property change must be an object.");
                string path = CommandArguments.RequiredString(change, "propertyPath");
                object value;
                if (!change.TryGetValue("value", out value))
                    throw new ProtocolException("INVALID_ARGUMENT", "value is required for " + path + ".");
                SerializedProperty property = RequireProperty(serializedObject, path, target);
                AssignValue(property, value);
                changed.Add(property.propertyPath);
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return Result(target, changed);
        }

        public static IDictionary<string, object> ResizeArray(UnityEngine.Object target, string requestedPath, int size)
        {
            if (size < 0 || size > 4096) throw new ProtocolException("INVALID_ARGUMENT", "size must be between 0 and 4096.");
            Undo.RecordObject(target, "Codex: Resize " + requestedPath);
            SerializedObject serializedObject = Prepare(target);
            SerializedProperty property = RequireProperty(serializedObject, requestedPath, target);
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
                throw new ProtocolException("INVALID_ARGUMENT", property.propertyPath + " is not an array or list.");
            property.arraySize = size;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return new Dictionary<string, object> { { "propertyPath", property.propertyPath }, { "size", size } };
        }

        public static IDictionary<string, object> SetArrayElement(
            UnityEngine.Object target, string requestedPath, int index, object value)
        {
            SerializedObject serializedObject = Prepare(target);
            SerializedProperty property = RequireProperty(serializedObject, requestedPath, target);
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
                throw new ProtocolException("INVALID_ARGUMENT", property.propertyPath + " is not an array or list.");
            if (index < 0 || index >= property.arraySize)
                throw new ProtocolException("INVALID_ARGUMENT", "index is outside the current array size.",
                    new Dictionary<string, object> { { "arraySize", property.arraySize } });
            Undo.RecordObject(target, "Codex: Set " + property.propertyPath + "[" + index + "]");
            SerializedProperty element = property.GetArrayElementAtIndex(index);
            AssignValue(element, value);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return new Dictionary<string, object>
            {
                { "propertyPath", property.propertyPath }, { "index", index },
                { "value", SerializedPropertySerializer.Value(element) }
            };
        }

        private static SerializedObject Prepare(UnityEngine.Object target)
        {
            SerializedObject result = new SerializedObject(target);
            result.UpdateIfRequiredOrScript();
            return result;
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serializedObject, string requestedPath, UnityEngine.Object target)
        {
            SerializedProperty property = FindProperty(serializedObject, requestedPath);
            if (property == null)
                throw new ProtocolException("PROPERTY_NOT_FOUND", "Serialized property '" + requestedPath
                    + "' was not found on " + target.GetType().FullName + ".");
            if (!property.editable)
                throw new ProtocolException("COMMAND_FAILED", "Serialized property '" + property.propertyPath + "' is read-only.");
            return property;
        }

        private static SerializedProperty FindProperty(SerializedObject serializedObject, string requestedPath)
        {
            SerializedProperty exact = serializedObject.FindProperty(requestedPath);
            if (exact != null) return exact;
            string normalizedRequest = Normalize(requestedPath);
            List<string> matchingPaths = new List<string>();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (string.Equals(Normalize(iterator.name), normalizedRequest, StringComparison.Ordinal)
                    || string.Equals(Normalize(iterator.displayName), normalizedRequest, StringComparison.Ordinal))
                    matchingPaths.Add(iterator.propertyPath);
            }
            if (matchingPaths.Count > 1)
                throw new ProtocolException("INVALID_ARGUMENT", "Property name '" + requestedPath
                    + "' is ambiguous. Use an exact propertyPath.",
                    new Dictionary<string, object> { { "matches", matchingPaths } });
            return matchingPaths.Count == 1 ? serializedObject.FindProperty(matchingPaths[0]) : null;
        }

        private static void AssignValue(SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (!(value is bool)) throw TypeMismatch(property, "bool"); property.boolValue = (bool)value; return;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    property.longValue = CommandArguments.ToLong(value, property.propertyPath); return;
                case SerializedPropertyType.Float:
                    property.doubleValue = CommandArguments.ToDouble(value, property.propertyPath); return;
                case SerializedPropertyType.String:
                    if (!(value is string)) throw TypeMismatch(property, "string"); property.stringValue = (string)value; return;
                case SerializedPropertyType.Vector2: property.vector2Value = CommandArguments.Vector2Value(value, property.propertyPath); return;
                case SerializedPropertyType.Vector3: property.vector3Value = CommandArguments.Vector3Value(value, property.propertyPath); return;
                case SerializedPropertyType.Vector4: property.vector4Value = CommandArguments.Vector4Value(value, property.propertyPath); return;
                case SerializedPropertyType.Color: property.colorValue = CommandArguments.ColorValue(value, property.propertyPath); return;
                case SerializedPropertyType.Rect: property.rectValue = CommandArguments.RectValue(value, property.propertyPath); return;
                case SerializedPropertyType.Bounds: property.boundsValue = CommandArguments.BoundsValue(value, property.propertyPath); return;
                case SerializedPropertyType.Vector2Int: property.vector2IntValue = CommandArguments.Vector2IntValue(value, property.propertyPath); return;
                case SerializedPropertyType.Vector3Int: property.vector3IntValue = CommandArguments.Vector3IntValue(value, property.propertyPath); return;
                case SerializedPropertyType.RectInt: property.rectIntValue = CommandArguments.RectIntValue(value, property.propertyPath); return;
                case SerializedPropertyType.BoundsInt: property.boundsIntValue = CommandArguments.BoundsIntValue(value, property.propertyPath); return;
                case SerializedPropertyType.Quaternion: property.quaternionValue = CommandArguments.QuaternionValue(value, property.propertyPath); return;
                case SerializedPropertyType.Enum: AssignEnum(property, value); return;
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.ExposedReference:
                    UnityEngine.Object reference = ResolveObjectReference(value, property);
                    ValidateObjectReferenceType(property, reference);
                    property.objectReferenceValue = reference;
                    return;
                case SerializedPropertyType.AnimationCurve: property.animationCurveValue = ParseCurve(value, property.propertyPath); return;
                case SerializedPropertyType.Gradient: property.gradientValue = ParseGradient(value, property.propertyPath); return;
                case SerializedPropertyType.Hash128:
                    string hash = value as string;
                    if (hash == null) throw TypeMismatch(property, "Hash128 string");
                    property.hash128Value = Hash128.Parse(hash); return;
                default:
                    throw new ProtocolException("CAPABILITY_UNAVAILABLE", "Serialized property type "
                        + property.propertyType + " is not safely writable by this Bridge build.");
            }
        }

        private static UnityEngine.Object ResolveObjectReference(object value, SerializedProperty property)
        {
            if (value == null) return null;
            IDictionary<string, object> reference = value as IDictionary<string, object>;
            if (reference == null) throw TypeMismatch(property, "object reference descriptor");
            string assetPath = CommandArguments.OptionalString(reference, "assetPath");
            string guid = CommandArguments.OptionalString(reference, "guid");
            UnityEngine.Object resolved;
            if (assetPath != null || guid != null)
            {
                string path = AssetResolver.ResolvePath(reference);
                resolved = AssetDatabase.LoadMainAssetAtPath(path);
            }
            else
            {
                string objectId;
                if (CommandArguments.TryObjectId(reference, "instanceId", out objectId))
                    resolved = UnityObjectId.Resolve(objectId);
                else
                    resolved = GameObjectResolver.Resolve(reference);
            }
            if (resolved == null) throw new ProtocolException("OBJECT_NOT_FOUND", "Object reference could not be resolved.");
            return resolved;
        }

        private static void ValidateObjectReferenceType(SerializedProperty property, UnityEngine.Object value)
        {
            if (value == null) return;
            string serializedType = property.type;
            const string prefix = "PPtr<$";
            if (string.IsNullOrEmpty(serializedType) || !serializedType.StartsWith(prefix, StringComparison.Ordinal)
                || !serializedType.EndsWith(">", StringComparison.Ordinal)) return;
            string expected = serializedType.Substring(prefix.Length, serializedType.Length - prefix.Length - 1);
            if (expected == "Object" || TypeMatches(value.GetType(), expected)) return;
            throw new ProtocolException("INVALID_ARGUMENT",
                "Serialized property '" + property.propertyPath + "' does not accept " + value.GetType().FullName + ".",
                new Dictionary<string, object>
                {
                    { "expectedType", expected }, { "actualType", value.GetType().FullName }
                });
        }

        private static bool TypeMatches(Type actual, string expected)
        {
            for (Type current = actual; current != null; current = current.BaseType)
            {
                if (string.Equals(current.Name, expected, StringComparison.Ordinal)
                    || string.Equals(current.FullName, expected, StringComparison.Ordinal)) return true;
                foreach (Type interfaceType in current.GetInterfaces())
                    if (string.Equals(interfaceType.Name, expected, StringComparison.Ordinal)
                        || string.Equals(interfaceType.FullName, expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void AssignEnum(SerializedProperty property, object value)
        {
            string enumName = value as string;
            if (enumName != null)
            {
                for (int index = 0; index < property.enumNames.Length; index++)
                    if (string.Equals(property.enumNames[index], enumName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.enumDisplayNames[index], enumName, StringComparison.OrdinalIgnoreCase))
                    { property.enumValueIndex = index; return; }
                throw new ProtocolException("INVALID_ARGUMENT", "Unknown enum value '" + enumName + "'.",
                    new Dictionary<string, object> { { "allowedValues", property.enumNames } });
            }
            int enumIndex = CommandArguments.ToInt(value, property.propertyPath);
            if (enumIndex < 0 || enumIndex >= property.enumNames.Length)
                throw new ProtocolException("INVALID_ARGUMENT", "Enum index is outside the allowed range.");
            property.enumValueIndex = enumIndex;
        }

        private static AnimationCurve ParseCurve(object value, string name)
        {
            IDictionary<string, object> data = value as IDictionary<string, object>;
            if (data == null) throw new ProtocolException("INVALID_ARGUMENT", name + " must contain a keys array.");
            IList<object> keys = CommandArguments.RequiredArray(data, "keys");
            if (keys.Count > 256) throw new ProtocolException("INVALID_ARGUMENT", "AnimationCurve supports at most 256 keys per request.");
            List<Keyframe> parsed = new List<Keyframe>();
            foreach (object item in keys)
            {
                IDictionary<string, object> key = item as IDictionary<string, object>;
                if (key == null) throw new ProtocolException("INVALID_ARGUMENT", "AnimationCurve keys must be objects.");
                parsed.Add(new Keyframe(
                    (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(key, "time", name), name + ".time"),
                    (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(key, "value", name), name + ".value"),
                    (float)CommandArguments.OptionalDouble(key, "inTangent", 0),
                    (float)CommandArguments.OptionalDouble(key, "outTangent", 0)));
            }
            AnimationCurve curve = new AnimationCurve(parsed.ToArray());
            string pre = CommandArguments.OptionalString(data, "preWrapMode");
            string post = CommandArguments.OptionalString(data, "postWrapMode");
            WrapMode mode;
            if (pre != null && Enum.TryParse(pre, true, out mode)) curve.preWrapMode = mode;
            if (post != null && Enum.TryParse(post, true, out mode)) curve.postWrapMode = mode;
            return curve;
        }

        private static Gradient ParseGradient(object value, string name)
        {
            IDictionary<string, object> data = value as IDictionary<string, object>;
            if (data == null) throw new ProtocolException("INVALID_ARGUMENT", name + " must be a Gradient object.");
            IList<object> colorItems = CommandArguments.RequiredArray(data, "colorKeys");
            IList<object> alphaItems = CommandArguments.RequiredArray(data, "alphaKeys");
            if (colorItems.Count > 8 || alphaItems.Count > 8) throw new ProtocolException("INVALID_ARGUMENT", "Gradient supports at most 8 color and alpha keys.");
            List<GradientColorKey> colorKeys = new List<GradientColorKey>();
            foreach (object item in colorItems)
            {
                IDictionary<string, object> key = item as IDictionary<string, object>;
                if (key == null) throw new ProtocolException("INVALID_ARGUMENT", "Gradient color keys must be objects.");
                colorKeys.Add(new GradientColorKey(
                    CommandArguments.ColorValue(CommandArguments.RequiredValue(key, "color", name), name + ".color"),
                    (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(key, "time", name), name + ".time")));
            }
            List<GradientAlphaKey> alphaKeys = new List<GradientAlphaKey>();
            foreach (object item in alphaItems)
            {
                IDictionary<string, object> key = item as IDictionary<string, object>;
                if (key == null) throw new ProtocolException("INVALID_ARGUMENT", "Gradient alpha keys must be objects.");
                alphaKeys.Add(new GradientAlphaKey(
                    (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(key, "alpha", name), name + ".alpha"),
                    (float)CommandArguments.ToDouble(CommandArguments.RequiredValue(key, "time", name), name + ".time")));
            }
            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
            string modeName = CommandArguments.OptionalString(data, "mode");
            GradientMode mode;
            if (modeName != null && Enum.TryParse(modeName, true, out mode)) gradient.mode = mode;
            return gradient;
        }

        private static IDictionary<string, object> Result(UnityEngine.Object target, IEnumerable<string> changed)
        {
            Component component = target as Component;
            return new Dictionary<string, object>
            {
                { "target", component == null ? AssetResolver.Reference(target) : UnityObjectSerializer.ComponentValue(component) },
                { "changedProperties", new List<string>(changed) }
            };
        }

        private static ProtocolException TypeMismatch(SerializedProperty property, string expected)
        { return new ProtocolException("INVALID_ARGUMENT", property.propertyPath + " expects " + expected + "."); }

        private static string Normalize(string value)
        {
            if (value.StartsWith("m_", StringComparison.Ordinal)) value = value.Substring(2);
            StringBuilder result = new StringBuilder(value.Length);
            foreach (char character in value)
                if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
            return result.ToString();
        }
    }
}
