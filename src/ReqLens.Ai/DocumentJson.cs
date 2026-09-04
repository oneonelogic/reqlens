using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.Runtime.Documents;

namespace ReqLens.Ai;

/// <summary>
/// Bridges System.Text.Json and the AWS SDK's <see cref="Document"/> union, which is what the
/// Converse API uses for both the tool schema going out and the tool payload coming back.
/// </summary>
/// <remarks>
/// The SDK offers Document.FromObject, but it reflects over CLR property names - which would tie
/// the wire format of the schema to C# identifiers and quietly break the moment a field name is
/// not a legal identifier. Going through JSON keeps the schema authored as JSON.
/// </remarks>
internal static class DocumentJson
{
    public static Document FromNode(JsonNode node) => FromElement(JsonSerializer.Deserialize<JsonElement>(node));

    public static Document FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new Document(element.EnumerateObject()
            .ToDictionary(p => p.Name, p => FromElement(p.Value))),

        JsonValueKind.Array => new Document(element.EnumerateArray().Select(FromElement).ToList()),

        JsonValueKind.String => new Document(element.GetString()!),

        JsonValueKind.Number => element.TryGetInt32(out var i)
            ? new Document(i)
            : new Document(element.GetDouble()),

        JsonValueKind.True => new Document(true),
        JsonValueKind.False => new Document(false),

        _ => default // Null and Undefined both land here; default(Document) is the null variant.
    };

    public static JsonNode? ToNode(Document document)
    {
        if (document.IsDictionary())
        {
            var obj = new JsonObject();
            foreach (var (key, value) in document.AsDictionary())
                obj[key] = ToNode(value);
            return obj;
        }

        if (document.IsList())
            return new JsonArray(document.AsList().Select(ToNode).ToArray());

        if (document.IsString()) return JsonValue.Create(document.AsString());
        if (document.IsBool()) return JsonValue.Create(document.AsBool());
        if (document.IsInt()) return JsonValue.Create(document.AsInt());
        if (document.IsLong()) return JsonValue.Create(document.AsLong());
        if (document.IsDouble()) return JsonValue.Create(document.AsDouble());

        return null;
    }
}
