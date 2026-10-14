using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Compliance.Invoices;

/// <summary>Lecture tolérante des JSON Paperasse (un nombre peut apparaître là où l'on attend du texte).</summary>
public static class PaperasseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new LenientStringConverter() },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static InvoiceDocument ReadInvoice(string json) =>
        JsonSerializer.Deserialize<InvoiceDocument>(json, Options) ?? throw new JsonException("Facture vide");

    public static CompanyProfile ReadCompany(string json) =>
        JsonSerializer.Deserialize<CompanyProfile>(json, Options) ?? throw new JsonException("company.json vide");

    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => SkipAndReturnNull(ref reader),
            };

        private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
        {
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
