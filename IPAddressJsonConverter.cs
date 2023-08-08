using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALXR
{
    public sealed class IPAddressJsonConverter : JsonConverter<IPAddress>
    {
        public override IPAddress Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var str = reader.GetString();
            if (str == null)
                return new IPAddress(new byte[] { 127, 0, 0, 1 });
            return IPAddress.Parse(str);
        }

        public override void Write(
            Utf8JsonWriter writer,
            IPAddress temperature,
            JsonSerializerOptions options) =>
                writer.WriteStringValue(temperature.ToString());
    }
}
