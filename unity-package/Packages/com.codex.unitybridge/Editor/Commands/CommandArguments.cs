using System;
using System.Collections.Generic;
using System.Globalization;
using Codex.UnityBridge.Protocol;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal static class CommandArguments
    {
        public static bool Has(IDictionary<string, object> values, string name)
        {
            object value;
            return values.TryGetValue(name, out value) && value != null;
        }

        public static string RequiredString(IDictionary<string, object> values, string name)
        {
            object value;
            string text;
            if (!values.TryGetValue(name, out value)
                || (text = value as string) == null
                || string.IsNullOrWhiteSpace(text))
            {
                throw Invalid(name + " must be a non-empty string.");
            }
            return text;
        }

        public static string OptionalString(
            IDictionary<string, object> values,
            string name,
            string defaultValue = null)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                return defaultValue;
            }
            string text = value as string;
            if (text == null)
            {
                throw Invalid(name + " must be a string.");
            }
            return text;
        }

        public static int RequiredInt(IDictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                throw Invalid(name + " must be an integer.");
            }
            return ToInt(value, name);
        }

        public static int OptionalInt(
            IDictionary<string, object> values,
            string name,
            int defaultValue)
        {
            object value;
            return !values.TryGetValue(name, out value) || value == null
                ? defaultValue
                : ToInt(value, name);
        }

        public static long OptionalLong(
            IDictionary<string, object> values,
            string name,
            long defaultValue)
        {
            object value;
            return !values.TryGetValue(name, out value) || value == null
                ? defaultValue
                : ToLong(value, name);
        }

        public static double OptionalDouble(
            IDictionary<string, object> values,
            string name,
            double defaultValue)
        {
            object value;
            return !values.TryGetValue(name, out value) || value == null
                ? defaultValue
                : ToDouble(value, name);
        }

        public static bool TryInt(IDictionary<string, object> values, string name, out int result)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                result = 0;
                return false;
            }
            result = ToInt(value, name);
            return true;
        }

        public static bool TryObjectId(
            IDictionary<string, object> values,
            string name,
            out string result)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                result = null;
                return false;
            }

            string text = value as string;
            if (text != null)
            {
                text = text.Trim();
                if (text.Length == 0)
                {
                    throw Invalid(name + " must be a non-empty object ID string or a 32-bit integer.");
                }
                result = text;
                return true;
            }

            result = ToInt(value, name).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        public static bool OptionalBool(IDictionary<string, object> values, string name, bool defaultValue)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                return defaultValue;
            }
            if (!(value is bool))
            {
                throw Invalid(name + " must be a boolean.");
            }
            return (bool)value;
        }

        public static IDictionary<string, object> RequiredObject(
            IDictionary<string, object> values,
            string name)
        {
            object value;
            IDictionary<string, object> result;
            if (!values.TryGetValue(name, out value)
                || (result = value as IDictionary<string, object>) == null)
            {
                throw Invalid(name + " must be an object.");
            }
            return result;
        }

        public static IList<object> RequiredArray(
            IDictionary<string, object> values,
            string name)
        {
            object value;
            IList<object> result;
            if (!values.TryGetValue(name, out value) || (result = value as IList<object>) == null)
            {
                throw Invalid(name + " must be an array.");
            }
            return result;
        }

        public static IList<object> OptionalArray(
            IDictionary<string, object> values,
            string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
            {
                return new List<object>();
            }
            IList<object> result = value as IList<object>;
            if (result == null)
            {
                throw Invalid(name + " must be an array.");
            }
            return result;
        }

        public static Vector2 Vector2Value(object value, string name)
        {
            IDictionary<string, object> vector = value as IDictionary<string, object>;
            if (vector == null)
            {
                throw Invalid(name + " must be an object with finite x and y numbers.");
            }
            return new Vector2(
                ToFloat(RequiredValue(vector, "x", name), name + ".x"),
                ToFloat(RequiredValue(vector, "y", name), name + ".y"));
        }

        public static Vector3 Vector3Value(object value, string name)
        {
            IDictionary<string, object> vector = value as IDictionary<string, object>;
            if (vector == null)
            {
                throw Invalid(name + " must be an object with finite x, y, and z numbers.");
            }
            return new Vector3(
                ToFloat(RequiredValue(vector, "x", name), name + ".x"),
                ToFloat(RequiredValue(vector, "y", name), name + ".y"),
                ToFloat(RequiredValue(vector, "z", name), name + ".z"));
        }

        public static Color ColorValue(object value, string name)
        {
            IDictionary<string, object> color = value as IDictionary<string, object>;
            if (color == null)
            {
                throw Invalid(name + " must be an object with r, g, b, and optional a numbers.");
            }

            object alpha;
            float a = color.TryGetValue("a", out alpha) && alpha != null
                ? ToFloat(alpha, name + ".a")
                : 1f;
            return new Color(
                ToFloat(RequiredValue(color, "r", name), name + ".r"),
                ToFloat(RequiredValue(color, "g", name), name + ".g"),
                ToFloat(RequiredValue(color, "b", name), name + ".b"),
                a);
        }

        public static Vector4 Vector4Value(object value, string name)
        {
            IDictionary<string, object> vector = value as IDictionary<string, object>;
            if (vector == null) throw Invalid(name + " must be an object with finite x, y, z, and w numbers.");
            return new Vector4(
                ToFloat(RequiredValue(vector, "x", name), name + ".x"),
                ToFloat(RequiredValue(vector, "y", name), name + ".y"),
                ToFloat(RequiredValue(vector, "z", name), name + ".z"),
                ToFloat(RequiredValue(vector, "w", name), name + ".w"));
        }

        public static Quaternion QuaternionValue(object value, string name)
        {
            Vector4 vector = Vector4Value(value, name);
            return new Quaternion(vector.x, vector.y, vector.z, vector.w);
        }

        public static Vector2Int Vector2IntValue(object value, string name)
        {
            IDictionary<string, object> vector = value as IDictionary<string, object>;
            if (vector == null) throw Invalid(name + " must be an object with integer x and y values.");
            return new Vector2Int(
                ToInt(RequiredValue(vector, "x", name), name + ".x"),
                ToInt(RequiredValue(vector, "y", name), name + ".y"));
        }

        public static Vector3Int Vector3IntValue(object value, string name)
        {
            IDictionary<string, object> vector = value as IDictionary<string, object>;
            if (vector == null) throw Invalid(name + " must be an object with integer x, y, and z values.");
            return new Vector3Int(
                ToInt(RequiredValue(vector, "x", name), name + ".x"),
                ToInt(RequiredValue(vector, "y", name), name + ".y"),
                ToInt(RequiredValue(vector, "z", name), name + ".z"));
        }

        public static Rect RectValue(object value, string name)
        {
            IDictionary<string, object> rectangle = value as IDictionary<string, object>;
            if (rectangle == null) throw Invalid(name + " must contain x, y, width, and height.");
            return new Rect(
                ToFloat(RequiredValue(rectangle, "x", name), name + ".x"),
                ToFloat(RequiredValue(rectangle, "y", name), name + ".y"),
                ToFloat(RequiredValue(rectangle, "width", name), name + ".width"),
                ToFloat(RequiredValue(rectangle, "height", name), name + ".height"));
        }

        public static Bounds BoundsValue(object value, string name)
        {
            IDictionary<string, object> bounds = value as IDictionary<string, object>;
            if (bounds == null) throw Invalid(name + " must contain center and size vectors.");
            return new Bounds(
                Vector3Value(RequiredValue(bounds, "center", name), name + ".center"),
                Vector3Value(RequiredValue(bounds, "size", name), name + ".size"));
        }

        public static RectInt RectIntValue(object value, string name)
        {
            IDictionary<string, object> rectangle = value as IDictionary<string, object>;
            if (rectangle == null) throw Invalid(name + " must contain x, y, width, and height integers.");
            return new RectInt(
                ToInt(RequiredValue(rectangle, "x", name), name + ".x"),
                ToInt(RequiredValue(rectangle, "y", name), name + ".y"),
                ToInt(RequiredValue(rectangle, "width", name), name + ".width"),
                ToInt(RequiredValue(rectangle, "height", name), name + ".height"));
        }

        public static BoundsInt BoundsIntValue(object value, string name)
        {
            IDictionary<string, object> bounds = value as IDictionary<string, object>;
            if (bounds == null) throw Invalid(name + " must contain position and size integer vectors.");
            return new BoundsInt(
                Vector3IntValue(RequiredValue(bounds, "position", name), name + ".position"),
                Vector3IntValue(RequiredValue(bounds, "size", name), name + ".size"));
        }

        public static double ToDouble(object value, string name)
        {
            try
            {
                double result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    throw Invalid(name + " must be finite.");
                }
                return result;
            }
            catch (ProtocolException)
            {
                throw;
            }
            catch (Exception)
            {
                throw Invalid(name + " must be a number.");
            }
        }

        public static int ToInt(object value, string name)
        {
            double number = ToDouble(value, name);
            if (number < int.MinValue || number > int.MaxValue || Math.Truncate(number) != number)
            {
                throw Invalid(name + " must be a 32-bit integer.");
            }
            return (int)number;
        }

        public static long ToLong(object value, string name)
        {
            if (value is long) return (long)value;
            string text = value as string;
            long parsed;
            if (text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            double number = ToDouble(value, name);
            if (number < long.MinValue || number > long.MaxValue || Math.Truncate(number) != number)
            {
                throw Invalid(name + " must be an integer representable by Int64.");
            }
            return Convert.ToInt64(number);
        }

        private static float ToFloat(object value, string name)
        {
            double number = ToDouble(value, name);
            if (number < -float.MaxValue || number > float.MaxValue)
            {
                throw Invalid(name + " is outside the supported float range.");
            }
            return (float)number;
        }

        public static object RequiredValue(IDictionary<string, object> values, string key, string parentName)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                throw Invalid(parentName + "." + key + " is required.");
            }
            return value;
        }

        private static ProtocolException Invalid(string message)
        {
            return new ProtocolException("INVALID_ARGUMENT", message);
        }
    }
}
