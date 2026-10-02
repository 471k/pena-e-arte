using System.Text.Json.Serialization;

namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The exact JSON shape Meta's Data Deletion Request callback requires: a status page URL and a
/// confirmation code, with these snake_case names (not the API's usual camelCase).
/// </summary>
public record InstagramDataDeletionResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("confirmation_code")] string ConfirmationCode);
