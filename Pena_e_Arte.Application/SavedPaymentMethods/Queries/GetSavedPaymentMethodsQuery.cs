using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
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
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        return await db.SavedPaymentMethods
            .Where(s => s.ClientId == client.Id)
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => AddSavedPaymentMethodHandler.Map(s))
            .ToListAsync(ct);
    }
}
