using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// X API v2 app-only lookup by username. Access-tier requirements for this endpoint
/// have changed repeatedly on X's side — confirm the current tier before relying on
/// this; see XSocialOAuthProvider's header comment for the same caveat.
/// </summary>
public sealed class XBioChecker(
    IHttpClientFactory httpFactory,
    IOptions<XOptions> options) : ISocialBioChecker
{
    private readonly XOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.X;

    public bool IsSupported => !string.IsNullOrEmpty(_opts.BearerToken);

    public async Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("X");

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"https://api.twitter.com/2/users/by/username/{Uri.EscapeDataString(handle)}?user.fields=description");
        request.Headers.Authorization = new("Bearer", _opts.BearerToken);

        HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return false;

        UserEnvelopeDto? envelope = await response.Content.ReadFromJsonAsync<UserEnvelopeDto>(ct);

        string? description = envelope?.Data?.Description;
        return description is not null && description.Contains(code, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UserEnvelopeDto([property: JsonPropertyName("data")] UserDto? Data);
    private sealed record UserDto([property: JsonPropertyName("description")] string? Description);
}
