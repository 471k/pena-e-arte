using System.Text.Json;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.BookingDrafts;

/// <summary>What a draft payload may hold. Shared by the validator and the handler, so both refuse the same thing.</summary>
public static class BookingDraftRules
{
    public const int MaxPayloadLength = 16000;

    /// <summary>True when the payload is a JSON object. A draft is a form's values, never an array or a scalar.</summary>
    public static bool IsJsonObject(string payload)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the payload carries the safety notes field anywhere, at any depth and in any letter case. It is
    /// refused whatever its value, empty included: Phi's decision is that a draft never holds the key at all.
    /// </summary>
    public static bool HoldsExcludedField(string payload)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            return Holds(doc.RootElement);
        }
        catch (JsonException)
        {
            return false; // not JSON: IsJsonObject refuses it
        }
    }

    private static bool Holds(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(p =>
            string.Equals(p.Name, BookingDraft.ExcludedField, StringComparison.OrdinalIgnoreCase) || Holds(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(Holds),
        _ => false,
    };
}
