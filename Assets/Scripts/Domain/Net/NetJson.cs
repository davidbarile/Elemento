using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Elemento.Domain.Net
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NetJsonNameAttribute : Attribute
    {
        public string Name { get; }

        public NetJsonNameAttribute(string inName)
        {
            this.Name = inName;
        }
    }

    public static class NetJson
    {
        public static string Serialize<T>(T inValue)
        {
            StringBuilder builder = new();
            WriteValue(builder, inValue);
            return builder.ToString();
        }

        public static T Deserialize<T>(string inJson)
        {
            if (inJson == null)
                throw new ArgumentNullException(nameof(inJson));

            var parser = new JsonParser(inJson);
            return (T)ConvertValue(parser.Parse(), typeof(T));
        }

        private static void WriteValue(StringBuilder inBuilder, object inValue)
        {
            if (inValue == null)
            {
                inBuilder.Append("null");
                return;
            }

            if (inValue is string text)
            {
                WriteString(inBuilder, text);
                return;
            }
            if (inValue is bool boolean)
            {
                inBuilder.Append(boolean ? "true" : "false");
                return;
            }

            Type type = inValue.GetType();
            if (type.IsEnum)
            {
                inBuilder.Append(Convert.ToInt64(inValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (IsNumber(type))
            {
                if (inValue is float single && (float.IsNaN(single) || float.IsInfinity(single)))
                    throw new InvalidOperationException("JSON cannot represent a non-finite number.");
                if (inValue is double real && (double.IsNaN(real) || double.IsInfinity(real)))
                    throw new InvalidOperationException("JSON cannot represent a non-finite number.");

                inBuilder.Append(Convert.ToString(inValue, CultureInfo.InvariantCulture));
                return;
            }
            if (inValue is IEnumerable values)
            {
                WriteArray(inBuilder, values);
                return;
            }

            WriteObject(inBuilder, inValue, type);
        }

        private static void WriteArray(StringBuilder inBuilder, IEnumerable inValues)
        {
            inBuilder.Append('[');
            bool first = true;
            foreach (var value in inValues)
            {
                if (!first)
                    inBuilder.Append(',');
                first = false;
                WriteValue(inBuilder, value);
            }
            inBuilder.Append(']');
        }

        private static void WriteObject(StringBuilder inBuilder, object inValue, Type inType)
        {
            inBuilder.Append('{');
            FieldInfo[] fields = inType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (inFirst, inSecond) => string.CompareOrdinal(inFirst.Name, inSecond.Name));
            for (int i = 0; i < fields.Length; ++i)
            {
                if (i > 0)
                    inBuilder.Append(',');
                WriteString(inBuilder, GetWireName(fields[i]));
                inBuilder.Append(':');
                WriteValue(inBuilder, fields[i].GetValue(inValue));
            }
            inBuilder.Append('}');
        }

        private static void WriteString(StringBuilder inBuilder, string inValue)
        {
            inBuilder.Append('"');
            foreach (char character in inValue)
            {
                switch (character)
                {
                    case '"': inBuilder.Append("\\\""); break;
                    case '\\': inBuilder.Append("\\\\"); break;
                    case '\b': inBuilder.Append("\\b"); break;
                    case '\f': inBuilder.Append("\\f"); break;
                    case '\n': inBuilder.Append("\\n"); break;
                    case '\r': inBuilder.Append("\\r"); break;
                    case '\t': inBuilder.Append("\\t"); break;
                    default:
                        if (character < 32)
                            inBuilder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            inBuilder.Append(character);
                        break;
                }
            }
            inBuilder.Append('"');
        }

        private static bool IsNumber(Type inType)
        {
            switch (Type.GetTypeCode(inType))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static object ConvertValue(object inValue, Type inTargetType)
        {
            Type nullableType = Nullable.GetUnderlyingType(inTargetType);
            if (nullableType != null)
                return inValue == null ? null : ConvertValue(inValue, nullableType);
            if (inValue == null)
                return inTargetType.IsValueType ? Activator.CreateInstance(inTargetType) : null;
            if (inTargetType == typeof(string))
                return (string)inValue;
            if (inTargetType.IsEnum)
                return Enum.ToObject(inTargetType, Convert.ToInt32(inValue, CultureInfo.InvariantCulture));
            if (inTargetType.IsPrimitive || inTargetType == typeof(decimal))
                return Convert.ChangeType(inValue, inTargetType, CultureInfo.InvariantCulture);
            if (inTargetType.IsArray)
            {
                var source = (List<object>)inValue;
                Type itemType = inTargetType.GetElementType();
                Array array = Array.CreateInstance(itemType, source.Count);
                for (int i = 0; i < source.Count; ++i)
                    array.SetValue(ConvertValue(source[i], itemType), i);
                return array;
            }
            if (typeof(IList).IsAssignableFrom(inTargetType))
            {
                var source = (List<object>)inValue;
                var list = (IList)Activator.CreateInstance(inTargetType);
                Type itemType = inTargetType.GetGenericArguments()[0];
                foreach (var item in source)
                    list.Add(ConvertValue(item, itemType));
                return list;
            }

            var values = (Dictionary<string, object>)inValue;
            object target = Activator.CreateInstance(inTargetType);
            foreach (var field in inTargetType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (values.TryGetValue(GetWireName(field), out object value))
                    field.SetValue(target, ConvertValue(value, field.FieldType));
            }
            return target;
        }

        private static string GetWireName(FieldInfo inField)
        {
            return inField.GetCustomAttribute<NetJsonNameAttribute>()?.Name ?? inField.Name;
        }

        private sealed class JsonParser
        {
            private readonly string json;
            private int position;

            public JsonParser(string inJson)
            {
                this.json = inJson;
            }

            public object Parse()
            {
                object value = ReadValue();
                SkipWhitespace();
                if (this.position != this.json.Length)
                    throw Error("Unexpected trailing JSON content.");
                return value;
            }

            private object ReadValue()
            {
                SkipWhitespace();
                if (this.position >= this.json.Length)
                    throw Error("Unexpected end of JSON.");

                switch (this.json[this.position])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                Dictionary<string, object> values = new(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();
                if (TryRead('}'))
                    return values;

                while (true)
                {
                    SkipWhitespace();
                    string name = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    values[name] = ReadValue();
                    SkipWhitespace();
                    if (TryRead('}'))
                        return values;
                    Expect(',');
                }
            }

            private List<object> ReadArray()
            {
                List<object> values = new();
                Expect('[');
                SkipWhitespace();
                if (TryRead(']'))
                    return values;

                while (true)
                {
                    values.Add(ReadValue());
                    SkipWhitespace();
                    if (TryRead(']'))
                        return values;
                    Expect(',');
                }
            }

            private string ReadString()
            {
                Expect('"');
                StringBuilder value = new();
                while (this.position < this.json.Length)
                {
                    char character = this.json[this.position++];
                    if (character == '"')
                        return value.ToString();
                    if (character != '\\')
                    {
                        value.Append(character);
                        continue;
                    }

                    if (this.position >= this.json.Length)
                        throw Error("Invalid JSON string escape.");
                    char escaped = this.json[this.position++];
                    switch (escaped)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u': value.Append(ReadUnicodeCharacter()); break;
                        default: throw Error("Invalid JSON string escape.");
                    }
                }

                throw Error("Unterminated JSON string.");
            }

            private char ReadUnicodeCharacter()
            {
                if (this.position + 4 > this.json.Length)
                    throw Error("Invalid JSON unicode escape.");

                string digits = this.json.Substring(this.position, 4);
                this.position += 4;
                if (!ushort.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
                    throw Error("Invalid JSON unicode escape.");
                return (char)value;
            }

            private object ReadNumber()
            {
                int start = this.position;
                while (this.position < this.json.Length)
                {
                    char character = this.json[this.position];
                    if ((character < '0' || character > '9')
                        && character != '-' && character != '+' && character != '.'
                        && character != 'e' && character != 'E')
                    {
                        break;
                    }
                    ++this.position;
                }

                string number = this.json.Substring(start, this.position - start);
                if (number.IndexOf('.') >= 0 || number.IndexOf('e') >= 0 || number.IndexOf('E') >= 0)
                {
                    if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                        return real;
                }
                else if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                {
                    return integer;
                }

                throw Error("Invalid JSON number.");
            }

            private void ReadLiteral(string inLiteral)
            {
                if (this.position + inLiteral.Length > this.json.Length
                    || string.CompareOrdinal(this.json, this.position, inLiteral, 0, inLiteral.Length) != 0)
                {
                    throw Error("Invalid JSON literal.");
                }
                this.position += inLiteral.Length;
            }

            private void SkipWhitespace()
            {
                while (this.position < this.json.Length && char.IsWhiteSpace(this.json[this.position]))
                    ++this.position;
            }

            private bool TryRead(char inCharacter)
            {
                if (this.position >= this.json.Length || this.json[this.position] != inCharacter)
                    return false;
                ++this.position;
                return true;
            }

            private void Expect(char inCharacter)
            {
                if (!TryRead(inCharacter))
                    throw Error("Expected '" + inCharacter + "'.");
            }

            private FormatException Error(string inMessage)
            {
                return new FormatException(inMessage + " Position: " + this.position.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
