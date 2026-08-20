using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class SerializedPropertySerializer
    {
        private const long MaxSafeJavaScriptInteger = 9007199254740991L;
        private const int MaxArrayPreviewItems = 32;

        public static IDictionary<string, object> ComponentProperties(
            Component component,
            int maxDepth,
            int maxItems,
            bool includeHidden)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty iterator = serializedObject.GetIterator();
            List<object> properties = new List<object>();
            bool enterChildren = true;
            bool truncated = false;

            while (MoveNext(iterator, enterChildren, includeHidden))
            {
                if (iterator.depth > maxDepth)
                {
                    enterChildren = false;
                    continue;
                }
                if (properties.Count >= maxItems)
                {
                    truncated = true;
                    break;
                }

                SerializedProperty copy = iterator.Copy();
                properties.Add(Describe(copy));
                enterChildren = copy.depth < maxDepth && !IsArray(copy);
            }

            return new Dictionary<string, object>
            {
                { "component", UnityObjectSerializer.ComponentValue(component) },
                { "properties", properties },
                { "count", properties.Count },
                { "truncated", truncated },
                { "maxDepth", maxDepth },
                { "maxItems", maxItems },
                { "includeHidden", includeHidden }
            };
        }

        public static IDictionary<string, object> Describe(SerializedProperty property)
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "propertyPath", property.propertyPath },
                { "displayName", property.displayName },
                { "propertyType", property.propertyType.ToString() },
                { "editable", property.editable },
                { "depth", property.depth },
                { "isArray", IsArray(property) }
            };
            if (IsArray(property))
            {
                int count = Math.Min(property.arraySize, MaxArrayPreviewItems);
                List<object> elements = new List<object>();
                for (int index = 0; index < count; index++)
                {
                    SerializedProperty element = property.GetArrayElementAtIndex(index);
                    elements.Add(new Dictionary<string, object>
                    {
                        { "index", index },
                        { "propertyPath", element.propertyPath },
                        { "propertyType", element.propertyType.ToString() },
                        { "value", Value(element) }
                    });
                }
                result["arraySize"] = property.arraySize;
                result["elements"] = elements;
                result["elementsTruncated"] = property.arraySize > count;
            }
            else
            {
                result["value"] = Value(property);
            }
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                result["allowedValues"] = property.enumNames;
                result["allowedDisplayValues"] = property.enumDisplayNames;
            }
            return result;
        }

        public static object Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean: return property.boolValue;
                case SerializedPropertyType.Integer: return SafeInteger(property.longValue);
                case SerializedPropertyType.Float: return property.doubleValue;
                case SerializedPropertyType.String: return property.stringValue;
                case SerializedPropertyType.Color: return ColorValue(property.colorValue);
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.ExposedReference:
                    return ObjectReference(property.objectReferenceValue);
                case SerializedPropertyType.LayerMask: return property.intValue;
                case SerializedPropertyType.Enum:
                    return new Dictionary<string, object>
                    {
                        { "index", property.enumValueIndex },
                        { "name", property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                            ? property.enumNames[property.enumValueIndex] : null }
                    };
                case SerializedPropertyType.Vector2: return Vector2Value(property.vector2Value);
                case SerializedPropertyType.Vector3: return UnityObjectSerializer.Vector3Value(property.vector3Value);
                case SerializedPropertyType.Vector4: return Vector4Value(property.vector4Value);
                case SerializedPropertyType.Rect: return RectValue(property.rectValue);
                case SerializedPropertyType.ArraySize: return property.intValue;
                case SerializedPropertyType.Character: return property.intValue;
                case SerializedPropertyType.AnimationCurve: return CurveValue(property.animationCurveValue);
                case SerializedPropertyType.Bounds: return BoundsValue(property.boundsValue);
                case SerializedPropertyType.Gradient: return GradientValue(property.gradientValue);
                case SerializedPropertyType.Quaternion: return QuaternionValue(property.quaternionValue);
                case SerializedPropertyType.Vector2Int: return Vector2IntValue(property.vector2IntValue);
                case SerializedPropertyType.Vector3Int: return Vector3IntValue(property.vector3IntValue);
                case SerializedPropertyType.RectInt: return RectIntValue(property.rectIntValue);
                case SerializedPropertyType.BoundsInt: return BoundsIntValue(property.boundsIntValue);
                case SerializedPropertyType.Hash128: return property.hash128Value.ToString();
                case SerializedPropertyType.FixedBufferSize: return property.fixedBufferSize;
                case SerializedPropertyType.ManagedReference:
                    return new Dictionary<string, object>
                    {
                        { "fullTypeName", property.managedReferenceFullTypename },
                        { "fieldTypeName", property.managedReferenceFieldTypename }
                    };
                default: return null;
            }
        }

        private static bool MoveNext(SerializedProperty property, bool enterChildren, bool includeHidden)
        {
            return includeHidden ? property.Next(enterChildren) : property.NextVisible(enterChildren);
        }

        private static bool IsArray(SerializedProperty property)
        {
            return property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        private static object SafeInteger(long value)
        {
            return value > MaxSafeJavaScriptInteger || value < -MaxSafeJavaScriptInteger
                ? (object)value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value;
        }

        private static object ObjectReference(UnityEngine.Object value)
        {
            if (value == null) return null;
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "instanceId", UnityObjectId.Get(value) },
                { "name", value.name },
                { "type", value.GetType().FullName }
            };
            string assetPath = AssetDatabase.GetAssetPath(value);
            if (!string.IsNullOrEmpty(assetPath))
            {
                result["assetPath"] = assetPath;
                result["guid"] = AssetDatabase.AssetPathToGUID(assetPath);
            }
            GameObject gameObject = value as GameObject;
            Component component = value as Component;
            if (component != null) gameObject = component.gameObject;
            if (gameObject != null && gameObject.scene.IsValid())
            {
                result["hierarchyPath"] = GameObjectResolver.HierarchyPath(gameObject);
                result["scenePath"] = gameObject.scene.path;
            }
            return result;
        }

        private static IDictionary<string, object> Vector2Value(Vector2 value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y } }; }
        private static IDictionary<string, object> Vector4Value(Vector4 value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "z", value.z }, { "w", value.w } }; }
        private static IDictionary<string, object> QuaternionValue(Quaternion value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "z", value.z }, { "w", value.w } }; }
        private static IDictionary<string, object> ColorValue(Color value)
        { return new Dictionary<string, object> { { "r", value.r }, { "g", value.g }, { "b", value.b }, { "a", value.a } }; }
        private static IDictionary<string, object> RectValue(Rect value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "width", value.width }, { "height", value.height } }; }
        private static IDictionary<string, object> Vector2IntValue(Vector2Int value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y } }; }
        private static IDictionary<string, object> Vector3IntValue(Vector3Int value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "z", value.z } }; }
        private static IDictionary<string, object> RectIntValue(RectInt value)
        { return new Dictionary<string, object> { { "x", value.x }, { "y", value.y }, { "width", value.width }, { "height", value.height } }; }
        private static IDictionary<string, object> BoundsValue(Bounds value)
        { return new Dictionary<string, object> { { "center", UnityObjectSerializer.Vector3Value(value.center) }, { "size", UnityObjectSerializer.Vector3Value(value.size) } }; }
        private static IDictionary<string, object> BoundsIntValue(BoundsInt value)
        { return new Dictionary<string, object> { { "position", Vector3IntValue(value.position) }, { "size", Vector3IntValue(value.size) } }; }

        private static object CurveValue(AnimationCurve curve)
        {
            List<object> keys = new List<object>();
            if (curve != null)
            {
                foreach (Keyframe key in curve.keys)
                {
                    keys.Add(new Dictionary<string, object>
                    {
                        { "time", key.time }, { "value", key.value },
                        { "inTangent", key.inTangent }, { "outTangent", key.outTangent }
                    });
                }
            }
            return new Dictionary<string, object>
            {
                { "keys", keys },
                { "preWrapMode", curve == null ? null : curve.preWrapMode.ToString() },
                { "postWrapMode", curve == null ? null : curve.postWrapMode.ToString() }
            };
        }

        private static object GradientValue(Gradient gradient)
        {
            if (gradient == null) return null;
            List<object> colorKeys = new List<object>();
            foreach (GradientColorKey key in gradient.colorKeys)
            {
                colorKeys.Add(new Dictionary<string, object> { { "time", key.time }, { "color", ColorValue(key.color) } });
            }
            List<object> alphaKeys = new List<object>();
            foreach (GradientAlphaKey key in gradient.alphaKeys)
            {
                alphaKeys.Add(new Dictionary<string, object> { { "time", key.time }, { "alpha", key.alpha } });
            }
            return new Dictionary<string, object>
            {
                { "mode", gradient.mode.ToString() }, { "colorKeys", colorKeys }, { "alphaKeys", alphaKeys }
            };
        }
    }
}
