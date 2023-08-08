using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALXR
{
    public sealed class EnumStringConverter<TEnum> : JsonConverter<TEnum> where TEnum : Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string value = reader.GetString();

            foreach (var field in typeToConvert.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute)
                {
                    if (attribute.Value == value)
                    {
                        return (TEnum)field.GetValue(null);
                    }
                }
            }

            throw new NotSupportedException($"Value '{value}' is not supported for enum '{typeToConvert.Name}'.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            Type valueType = value.GetType();
            FieldInfo field = valueType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(f => f.GetValue(null)?.Equals(value) == true);

            if (field != null && Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute)
            {
                writer.WriteStringValue(attribute.Value);
            }
            else
            {
                throw new NotSupportedException($"Value '{value}' is not supported for enum '{value.GetType().Name}'.");
            }
        }
    }
}