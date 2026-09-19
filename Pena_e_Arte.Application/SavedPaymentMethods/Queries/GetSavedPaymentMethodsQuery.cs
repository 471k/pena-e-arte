using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.SavedPaymentMethods.Queries;

/// <summary>Lists the caller's own saved cards at the current studio — a card saved at one
/// studio never appears at another (ADR-0001: per-studio POK merchant, tokens aren't portable
/// across studios).</summary>
public record GetSavedPaymentMethodsQuery : IRequest<List<SavedPaymentMethodResponse>>;

public class GetSavedPaymentMethodsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetSavedPaymentMethodsQuery, List<SavedPaymentMethodResponse>>
{
    public async Task<List<SavedPaymentMethodResponse>> Handle(GetSavedPaymentMethodsQuery query, CancellationToken ct)
    {
        // A client who hasn't joined a studio yet (or hasn't booked at the current one) has no
        // Client row here — that's "no saved cards", not a failure. Matches the same pattern
        // GetMyAppointmentsQuery/GetMyWaitlistEntriesQuery already use for "my X" list queries;
        // this one previously 404'd instead, which the frontend surfaced as a dead-end
        // "Failed to load" error with no way to recover short of joining a studio first.
        Client? client = await db.FindClientForUserAsync(currentUser, ct);
        if (client is null) return [];

        return await db.SavedPaymentMethods
            .Where(s => s.ClientId == client.Id)
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => AddSavedPaymentMethodHandler.Map(s))
            .ToListAsync(ct);
    }
}
