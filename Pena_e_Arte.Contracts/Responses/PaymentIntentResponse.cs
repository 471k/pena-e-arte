namespace Pena_e_Arte.Contracts.Responses;

public record PaymentIntentResponse(
    Guid PaymentId,
    string ClientToken,
    string Status,
    // What the card will be charged, so the card form can say it next to POK's widget (C-11,
    // 28 Oct 2026: the card box never showed the amount). Set by the client deposit flow.
    decimal? Amount = null,
    string? Currency = null);
