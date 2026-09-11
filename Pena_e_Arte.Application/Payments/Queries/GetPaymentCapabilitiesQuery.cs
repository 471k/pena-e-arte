using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentCapabilitiesQuery : IRequest<PaymentCapabilitiesResponse>;

/// <summary>
/// Card payments are only actually available when BOTH the provider supports them (a platform-
/// level capability) AND this specific studio has connected its own POK account (ADR-0001 — there
/// is no platform-level key, so a provider that supports card payments in general still can't take
/// one for a studio that never connected). Checking Capabilities alone was a real gap: it always
/// reports true for PokPaymentProvider regardless of connection state, so the Card tab stayed
/// visible for an unconnected studio and only failed at checkout with
/// PaymentProviderNotConnectedException — contradicting the Help copy that says clients see Cash
/// only until POK is connected.
/// </summary>
public class GetPaymentCapabilitiesHandler(IPaymentProvider paymentProvider, IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetPaymentCapabilitiesQuery, PaymentCapabilitiesResponse>
{
    public async Task<PaymentCapabilitiesResponse> Handle(GetPaymentCapabilitiesQuery query, CancellationToken ct)
    {
        if (!paymentProvider.Capabilities.SupportsAuthCapture)
            return new PaymentCapabilitiesResponse(CardPaymentsAvailable: false);

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct);
        bool hasCredentialRef = await db.StudioCredentialRefs
            .AnyAsync(c => c.StudioId == tenant.StudioId && c.Provider == CredentialProvider.Pok, ct);

        bool connected = studio?.PokMerchantId is not null && hasCredentialRef;
        return new PaymentCapabilitiesResponse(
            CardPaymentsAvailable: connected,
            PokEnvironment: connected ? paymentProvider.Capabilities.Environment : null);
    }
}
