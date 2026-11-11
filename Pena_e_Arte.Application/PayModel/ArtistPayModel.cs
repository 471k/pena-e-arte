using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.PayModel;

// ── GET /artists/{id}/pay-model (OwnerOnly) ──────────────────────────────────

/// <summary>
/// One artist's own pay terms (A07a). Owner-only and separate from ArtistResponse: PUT /artists/{id}
/// is open to the artist themselves, and ArtistResponse is readable by clients, so neither may carry it.
/// </summary>
public record GetArtistPayModelQuery(Guid ArtistId) : IRequest<ArtistPayModelResponse>;

public class GetArtistPayModelValidator : AbstractValidator<GetArtistPayModelQuery>
{
    public GetArtistPayModelValidator() => RuleFor(q => q.ArtistId).NotEmpty();
}

public class GetArtistPayModelHandler(IAppDbContext db) : IRequestHandler<GetArtistPayModelQuery, ArtistPayModelResponse>
{
    public async Task<ArtistPayModelResponse> Handle(GetArtistPayModelQuery query, CancellationToken ct) =>
        await db.Artists
            .Where(a => a.Id == query.ArtistId)
            .Select(a => new ArtistPayModelResponse(
                a.Id,
                a.PayArrangement == null ? null : a.PayArrangement.Value.ToString(),
                a.CommissionRate,
                a.FlatFeePerSession))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Artist), query.ArtistId);
}

// ── PUT /artists/{id}/pay-model (OwnerOnly) ──────────────────────────────────

/// <summary>Sets one artist's own pay terms (A07a). Null fields fall back to the studio's.</summary>
public record UpdateArtistPayModelCommand(Guid ArtistId, UpdateArtistPayModelRequest Request) : IRequest<ArtistPayModelResponse>;

public class UpdateArtistPayModelValidator : AbstractValidator<UpdateArtistPayModelCommand>
{
    public UpdateArtistPayModelValidator()
    {
        RuleFor(c => c.ArtistId).NotEmpty();
        RuleFor(c => c.Request.Arrangement)
            .Must(a => a is null || PayModelRules.TryParseArrangement(a, out _))
            .WithMessage("Arrangement must be Commission, BoothRent, FlatFeePerSession, Salaried, or empty for the studio's.");
        RuleFor(c => c.Request.CommissionRate).ValidCommissionRate();
        RuleFor(c => c.Request.FlatFeePerSession).ValidFlatFee();
    }
}

public class UpdateArtistPayModelHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateArtistPayModelCommand, ArtistPayModelResponse>
{
    public async Task<ArtistPayModelResponse> Handle(UpdateArtistPayModelCommand command, CancellationToken ct)
    {
        UpdateArtistPayModelRequest req = command.Request;
        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.ArtistId, ct)
            ?? throw new NotFoundException(nameof(Artist), command.ArtistId);

        if (req.FlatFeePerSession is decimal fee)
        {
            string currency = await db.Studios.Where(s => s.Id == tenant.StudioId).Select(s => s.Currency).SingleAsync(ct);
            if (!CurrencyCatalog.HasAtMostMinorUnits(fee, currency))
                throw new BusinessRuleViolationException($"The fee has more decimal places than {currency} allows.");
        }

        ArtistPayArrangement? arrangement = null;
        if (PayModelRules.TryParseArrangement(req.Arrangement, out ArtistPayArrangement parsed))
            arrangement = parsed;

        artist.PayArrangement = arrangement;
        artist.CommissionRate = req.CommissionRate;
        artist.FlatFeePerSession = req.FlatFeePerSession;
        artist.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ArtistPayModelResponse(artist.Id, arrangement?.ToString(), artist.CommissionRate, artist.FlatFeePerSession);
    }
}
