using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// IPokCardTokenService implementation. See that interface's doc comment for exactly which
/// pieces of this are confirmed against POK's real docs versus inferred from REST convention —
/// don't trust the inferred parts against production card traffic without a real sandbox
/// transaction first.
/// </summary>
public sealed class PokCardTokenService(PokAuthClient authClient) : IPokCardTokenService
{
    public async Task<PokTokenizedCard> TokenizeCardAsync(
        Guid studioId, string jwe, string? securityCode, PokCardBillingInfo billingInfo, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        // UNVERIFIED: neither this path nor this request shape is confirmed against POK's docs
        // (see IPokCardTokenService's doc comment) — inferred from sdk-orders' own
        // /merchants/{merchantId}/... convention, since a tokenized card is scoped to the
        // merchant that will charge it, same as an order is. Every field forwarded here is one
        // AddCardData (the widget's own onSuccess payload) actually returns — nothing invented.
        var body = new TokenizeCardRequestBody
        {
            CsFlexCard = new CsFlexCardBody { Jwe = jwe },
            SecurityCode = securityCode,
            BillingInfo = new BillingInfoBody
            {
                FirstName = billingInfo.FirstName,
                LastName = billingInfo.LastName,
                Email = billingInfo.Email,
                CountryCode = billingInfo.CountryCode,
                AdministrativeArea = billingInfo.AdministrativeArea,
                Locality = billingInfo.Locality,
                Address1 = billingInfo.Address1,
                PostalCode = billingInfo.PostalCode,
                PhoneNumber = billingInfo.PhoneNumber,
            }
        };

        using HttpRequestMessage req = new(HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/credit-debit-cards");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);

        TokenizeCardResponse result = await authClient.SendAsync<TokenizeCardResponse>(http, req, studioId, ct);
        CreditDebitCardDto card = result.Data?.CreditDebitCard
            ?? throw new ServiceUnavailableException("POK did not return a tokenized card id.");

        return new PokTokenizedCard(
            card.Id ?? throw new ServiceUnavailableException("POK did not return a tokenized card id."),
            card.Brand, card.MaskedPan, card.ExpiryMonth, card.ExpiryYear);
    }

    public async Task<PokPayerAuthSetup> SetupTokenizedThreeDsAsync(
        Guid studioId, string orderId, string cardTokenId, CancellationToken ct)
    {
        (_, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        // CONFIRMED path (quoted directly from POK's Flutter SDK docs, docs/payments/pok-
        // assessment.md's WebFetch trail): root-level, NOT under /merchants/{merchantId}/ like
        // every other call in this codebase's POK integration. Request body shape (sdkOrder.id)
        // is inferred, not confirmed — "your backend must create a new SDK order" is the only
        // documented hint that this call needs to reference one.
        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/credit-debit-cards/{cardTokenId}/setup-tokenized-3ds");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new SetupTokenizedThreeDsRequestBody
        {
            SdkOrder = new SdkOrderRefBody { Id = orderId }
        });

        SetupTokenizedThreeDsResponse result = await authClient.SendAsync<SetupTokenizedThreeDsResponse>(http, req, studioId, ct);
        PayerAuthenticationDto auth = result.Data?.PayerAuthentication
            ?? throw new ServiceUnavailableException("POK did not return a payer authentication setup.");

        if (auth.PayerAuthSetupReferenceId is null)
            throw new ServiceUnavailableException("POK did not return a payerAuthSetupReferenceId.");

        PokDeviceDataCollection? ddc = auth.DeviceDataCollection is { Url: not null, AccessToken: not null } d
            ? new PokDeviceDataCollection(d.Url, d.AccessToken)
            : null;

        return new PokPayerAuthSetup(auth.PayerAuthSetupReferenceId, ddc);
    }

    // --- Wire DTOs (see class doc comment — most of this shape is inferred, not confirmed) ---

    private sealed class TokenizeCardRequestBody
    {
        [JsonPropertyName("csFlexCard")] public CsFlexCardBody CsFlexCard { get; init; } = new();
        [JsonPropertyName("securityCode")] public string? SecurityCode { get; init; }
        [JsonPropertyName("billingInfo")] public BillingInfoBody BillingInfo { get; init; } = new();
    }

    private sealed class CsFlexCardBody
    {
        [JsonPropertyName("jwe")] public string Jwe { get; init; } = "";
    }

    private sealed class BillingInfoBody
    {
        [JsonPropertyName("firstName")] public string FirstName { get; init; } = "";
        [JsonPropertyName("lastName")] public string LastName { get; init; } = "";
        [JsonPropertyName("email")] public string Email { get; init; } = "";
        [JsonPropertyName("countryCode")] public string CountryCode { get; init; } = "";
        [JsonPropertyName("administrativeArea")] public string? AdministrativeArea { get; init; }
        [JsonPropertyName("locality")] public string? Locality { get; init; }
        [JsonPropertyName("address1")] public string? Address1 { get; init; }
        [JsonPropertyName("postalCode")] public string? PostalCode { get; init; }
        [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; init; }
    }

    private sealed class TokenizeCardResponse
    {
        [JsonPropertyName("data")] public TokenizeCardResponseData? Data { get; init; }
    }

    private sealed class TokenizeCardResponseData
    {
        [JsonPropertyName("creditDebitCard")] public CreditDebitCardDto? CreditDebitCard { get; init; }
    }

    private sealed class CreditDebitCardDto
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("brand")] public string? Brand { get; init; }
        // Field name guessed as "maskedPan" — POK's docs never show this response body at all;
        // if the real field is named differently (or absent), this simply stays null (see
        // PokTokenizedCard's own doc comment: a SavedPaymentMethod degrades gracefully to a null
        // display field rather than failing the save).
        [JsonPropertyName("maskedPan")] public string? MaskedPan { get; init; }
        [JsonPropertyName("expiryMonth")] public string? ExpiryMonth { get; init; }
        [JsonPropertyName("expiryYear")] public string? ExpiryYear { get; init; }
    }

    private sealed class SetupTokenizedThreeDsRequestBody
    {
        [JsonPropertyName("sdkOrder")] public SdkOrderRefBody SdkOrder { get; init; } = new();
    }

    private sealed class SdkOrderRefBody
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
    }

    private sealed class SetupTokenizedThreeDsResponse
    {
        [JsonPropertyName("data")] public SetupTokenizedThreeDsResponseData? Data { get; init; }
    }

    private sealed class SetupTokenizedThreeDsResponseData
    {
        [JsonPropertyName("payerAuthentication")] public PayerAuthenticationDto? PayerAuthentication { get; init; }
    }

    private sealed class PayerAuthenticationDto
    {
        // Confirmed field name (quoted verbatim from POK's Flutter SDK docs).
        [JsonPropertyName("payerAuthSetupReferenceId")] public string? PayerAuthSetupReferenceId { get; init; }
        [JsonPropertyName("deviceDataCollection")] public DeviceDataCollectionDto? DeviceDataCollection { get; init; }
    }

    private sealed class DeviceDataCollectionDto
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    }
}
