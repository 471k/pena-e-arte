using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// IPokCardTokenService implementation, built against POK's documented Card Tokenization API
/// (payments.doc.pokpay.io; endpoint list and shapes in the shared POK reference doc, Part 1 §4).
/// See that interface's doc comment for what a real staging run confirmed.
/// </summary>
public sealed class PokCardTokenService(PokAuthClient authClient, ILogger<PokCardTokenService> logger) : IPokCardTokenService
{
    public async Task<PokTokenizedCard> TokenizeCardAsync(
        Guid studioId, string jwe, string? securityCode, PokCardBillingInfo billingInfo, CancellationToken ct)
    {
        (_, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        // Root-level, not under /merchants/{merchantId}/. POK's docs also list csFlexCard's
        // expirationMonth/expirationYear/numericCardType as required, but AddCardForm never hands
        // them to the host (its onSuccess payload is exactly { csFlexCard: { jwe }, billingInfo,
        // securityCode }), and POK's own GuestCheckoutForm sends csFlexCard with the jwe alone to
        // /credit-debit-cards/guest — the jwe carries the whole encrypted card.
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

        using HttpRequestMessage req = new(HttpMethod.Post, $"{authClient.BaseUrl}/credit-debit-cards/tokenize-guest-card");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);

        // The response also carries holderFirstName/holderLastName/billingInfo; those are left out
        // of the DTO on purpose, so the cardholder's name and address are never kept or logged.
        TokenizeCardResponse result = await authClient.SendAsync<TokenizeCardResponse>(http, req, studioId, ct);
        string cardId = result.Data?.CreditDebitCard?.Id
            ?? throw new ServiceUnavailableException("POK did not return a tokenized card id.");

        // The tokenize response has no brand; get-guest-cards-information has the brand and the
        // masked number. Display only, so a failure here falls back to hiddenNumber, never fails the save.
        GuestCardDto? info = await TryGetCardInformationAsync(http, token, studioId, cardId, ct);

        // POK keeps no expiry date for a tokenized card (neither response has one).
        return new PokTokenizedCard(
            cardId,
            FormatBrand(info?.Brand),
            FormatMaskedNumber(info?.Number ?? result.Data!.CreditDebitCard!.HiddenNumber),
            ExpiryMonth: null,
            ExpiryYear: null);
    }

    public async Task<PokPayerAuthSetup> SetupTokenizedThreeDsAsync(
        Guid studioId, string orderId, string cardTokenId, CancellationToken ct)
    {
        (_, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        // Path and body ({ sdkOrder: { id } }) as documented. The SDK's payByCardToken then runs
        // device data collection, check-3ds-enrollment and guest-confirm in the browser.
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

    private async Task<GuestCardDto?> TryGetCardInformationAsync(
        HttpClient http, string token, Guid studioId, string cardId, CancellationToken ct)
    {
        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/credit-debit-cards/get-guest-cards-information");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new GuestCardsInformationRequestBody { CardIds = [cardId] });

        try
        {
            GuestCardsInformationResponse result =
                await authClient.SendAsync<GuestCardsInformationResponse>(http, req, studioId, ct);
            return result.Data?.Cards?.FirstOrDefault();
        }
        catch (ServiceUnavailableException)
        {
            // SendAsync has already logged the status code; nothing about the card is logged.
            logger.LogWarning("POK card information unavailable for studio {StudioId}; saving the card without its brand.", studioId);
            return null;
        }
    }

    /// <summary>"visa" → "Visa"; null or blank stays null so the UI shows "Card on file".</summary>
    internal static string? FormatBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return null;
        string trimmed = brand.Trim();
        return trimmed.Any(char.IsUpper) ? trimmed : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed);
    }

    /// <summary>"**** **** **** 0023" or "411111******0023" → "**** 0023". Anything without four
    /// trailing digits is dropped rather than shown as-is, so a full number can never be displayed.</summary>
    internal static string? FormatMaskedNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return null;
        string trimmed = number.Trim();
        if (trimmed.Length < 4 || !trimmed[^4..].All(char.IsAsciiDigit))
            return null;
        return $"**** {trimmed[^4..]}";
    }

    // --- Wire DTOs ---

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
        [JsonPropertyName("hiddenNumber")] public string? HiddenNumber { get; init; }
    }

    private sealed class GuestCardsInformationRequestBody
    {
        [JsonPropertyName("cardIds")] public string[] CardIds { get; init; } = [];
    }

    private sealed class GuestCardsInformationResponse
    {
        [JsonPropertyName("data")] public GuestCardsInformationResponseData? Data { get; init; }
    }

    private sealed class GuestCardsInformationResponseData
    {
        [JsonPropertyName("cards")] public List<GuestCardDto>? Cards { get; init; }
    }

    // billingInfo (name, address, email, phone) is left out on purpose, as in CreditDebitCardDto.
    private sealed class GuestCardDto
    {
        [JsonPropertyName("brand")] public string? Brand { get; init; }
        [JsonPropertyName("number")] public string? Number { get; init; }
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
        [JsonPropertyName("payerAuthSetupReferenceId")] public string? PayerAuthSetupReferenceId { get; init; }
        [JsonPropertyName("deviceDataCollection")] public DeviceDataCollectionDto? DeviceDataCollection { get; init; }
    }

    private sealed class DeviceDataCollectionDto
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    }
}
