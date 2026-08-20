using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Codex.UnityBridge.Protocol
{
    // Unity 2021 does not guarantee System.Text.Json. This deliberately small JSON
    // implementation keeps the package dependency-free and only handles JSON values.
    internal static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }

            using (StringReader reader = new StringReader(json))
            {
                Parser parser = new Parser(reader);
                object value = parser.ParseValue();
                parser.EnsureEndOfInput();
                return value;
            }
        }

        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder(256);
            Serializer.SerializeValue(value, builder);
            return builder.ToString();
        }

        private sealed class Parser
        {
            private readonly TextReader reader;
            private int nextCharacter = -2;

            public Parser(TextReader reader)
            {
                this.reader = reader;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                int character = Peek();
                switch (character)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (character == '-' || (character >= '0' && character <= '9'))
                        {
                            return ParseNumber();
                        }
                        throw Error("Expected a JSON value.");
                }
            }

            public void EnsureEndOfInput()
            {
                SkipWhitespace();
                if (Peek() != -1)
                {
                    throw Error("Unexpected data after JSON value.");
                }
            }

            private IDictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}')
                {
                    Read();
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("Expected an object property name.");
                    }
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    int delimiter = Read();
                    if (delimiter == '}')
                    {
                        return result;
                    }
                    if (delimiter != ',')
                    {
                        throw Error("Expected ',' or '}' in object.");
                    }
                }
            }

            private IList<object> ParseArray()
            {
                List<object> result = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']')
                {
                    Read();
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    int delimiter = Read();
                    if (delimiter == ']')
                    {
                        return result;
                    }
                    if (delimiter != ',')
                    {
                        throw Error("Expected ',' or ']' in array.");
                    }
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder result = new StringBuilder();
                while (true)
                {
                    int character = Read();
                    if (character == -1)
                    {
                        throw Error("Unterminated string.");
                    }
                    if (character == '"')
                    {
                        return result.ToString();
                    }
                    if (character < 0x20)
                    {
                        throw Error("Control characters are not permitted in strings.");
                    }
                    if (character != '\\')
                    {
                        result.Append((char)character);
                        continue;
                    }

                    int escaped = Read();
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u': result.Append(ParseUnicodeEscape()); break;
                        default: throw Error("Invalid escape sequence in string.");
                    }
                }
            }

            private char ParseUnicodeEscape()
            {
                int value = 0;
                for (int index = 0; index < 4; index++)
                {
                    int character = Read();
                    value <<= 4;
                    if (character >= '0' && character <= '9') value += character - '0';
                    else if (character >= 'a' && character <= 'f') value += character - 'a' + 10;
                    else if (character >= 'A' && character <= 'F') value += character - 'A' + 10;
                    else throw Error("Invalid Unicode escape sequence.");
                }
                return (char)value;
            }

            private object ParseNumber()
            {
                StringBuilder number = new StringBuilder();
                if (Peek() == '-') number.Append((char)Read());

                if (Peek() == '0')
                {
                    number.Append((char)Read());
                }
                else
                {
                    ReadDigits(number, true);
                }

                bool isFloatingPoint = false;
                if (Peek() == '.')
                {
                    isFloatingPoint = true;
                    number.Append((char)Read());
                    ReadDigits(number, true);
                }

                int exponent = Peek();
                if (exponent == 'e' || exponent == 'E')
                {
                    isFloatingPoint = true;
                    number.Append((char)Read());
                    if (Peek() == '+' || Peek() == '-') number.Append((char)Read());
                    ReadDigits(number, true);
                }

                string text = number.ToString();
                long integer;
                if (!isFloatingPoint && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return integer;
                }

                double floatingPoint;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatingPoint)
                    && !double.IsNaN(floatingPoint) && !double.IsInfinity(floatingPoint))
                {
                    return floatingPoint;
                }
                throw Error("Invalid JSON number.");
            }

            private void ReadDigits(StringBuilder result, bool requireOne)
            {
                int count = 0;
                while (Peek() >= '0' && Peek() <= '9')
                {
                    result.Append((char)Read());
                    count++;
                }
                if (requireOne && count == 0)
                {
                    throw Error("Expected a digit.");
                }
            }

            private void ReadLiteral(string literal)
            {
                for (int index = 0; index < literal.Length; index++)
                {
                    if (Read() != literal[index])
                    {
                        throw Error("Invalid JSON literal.");
                    }
                }
            }

            private void SkipWhitespace()
            {
                while (Peek() == ' ' || Peek() == '\t' || Peek() == '\r' || Peek() == '\n')
                {
                    Read();
                }
            }

            private void Expect(int expected)
            {
                if (Read() != expected)
                {
                    throw Error("Expected '" + (char)expected + "'.");
                }
            }

            private int Peek()
            {
                if (nextCharacter == -2)
                {
                    nextCharacter = reader.Read();
                }
                return nextCharacter;
            }

            private int Read()
            {
                int value = Peek();
                nextCharacter = -2;
                return value;
            }

            private static FormatException Error(string message)
            {
                return new FormatException(message);
            }
        }

        private static class Serializer
        {
            public static void SerializeValue(object value, StringBuilder builder)
            {
                if (value == null)
                {
                    builder.Append("null");
                    return;
                }

                string text = value as string;
                if (text != null)
                {
                    SerializeString(text, builder);
                    return;
                }

                if (value is bool)
                {
                    builder.Append((bool)value ? "true" : "false");
                    return;
                }

                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    SerializeObject(dictionary, builder);
                    return;
                }

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null)
                {
                    SerializeArray(enumerable, builder);
                    return;
                }

                if (value is Enum)
                {
                    SerializeString(value.ToString(), builder);
                    return;
                }

                IFormattable number = value as IFormattable;
                if (number != null && IsNumber(value))
                {
                    string formatted = number.ToString(null, CultureInfo.InvariantCulture);
                    if (formatted == "NaN" || formatted == "Infinity" || formatted == "-Infinity")
                    {
                        throw new InvalidOperationException("JSON cannot represent NaN or infinity.");
                    }
                    builder.Append(formatted);
                    return;
                }

                throw new InvalidOperationException("Cannot serialize value of type " + value.GetType().FullName + ".");
            }

            private static void SerializeObject(IDictionary dictionary, StringBuilder builder)
            {
                builder.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!(entry.Key is string))
                    {
                        throw new InvalidOperationException("JSON object keys must be strings.");
                    }
                    if (!first) builder.Append(',');
                    SerializeString((string)entry.Key, builder);
                    builder.Append(':');
                    SerializeValue(entry.Value, builder);
                    first = false;
                }
                builder.Append('}');
            }

            private static void SerializeArray(IEnumerable values, StringBuilder builder)
            {
                builder.Append('[');
                bool first = true;
                foreach (object value in values)
                {
                    if (!first) builder.Append(',');
                    SerializeValue(value, builder);
                    first = false;
                }
                builder.Append(']');
            }

            private static void SerializeString(string value, StringBuilder builder)
            {
                builder.Append('"');
                foreach (char character in value)
                {
                    switch (character)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (character < 0x20)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }
                            break;
                    }
                }
                builder.Append('"');
            }

            private static bool IsNumber(object value)
            {
                return value is byte || value is sbyte || value is short || value is ushort
                    || value is int || value is uint || value is long || value is ulong
                    || value is float || value is double || value is decimal;
            }
        }
    }
}
