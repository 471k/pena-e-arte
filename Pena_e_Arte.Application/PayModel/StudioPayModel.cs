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
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.PayModel;

// ── GET /studios/me/pay-model (OwnerOnly) ────────────────────────────────────

/// <summary>The studio's default pay terms (A07a), for the owner's Studio settings card.</summary>
public record GetStudioPayModelQuery : IRequest<StudioPayModelResponse>;

public class GetStudioPayModelValidator : AbstractValidator<GetStudioPayModelQuery>
{
    // No input: the studio comes from the caller's tenant.
}

public class GetStudioPayModelHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetStudioPayModelQuery, StudioPayModelResponse>
{
    public async Task<StudioPayModelResponse> Handle(GetStudioPayModelQuery query, CancellationToken ct) =>
        await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => new StudioPayModelResponse(
                s.DefaultPayArrangement.ToString(),
                s.DefaultCommissionRate,
                s.DefaultFlatFeePerSession,
                s.PayModelReviewedAt,
                s.Currency))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);
}

// ── PUT /studios/me/pay-model (OwnerOnly) ────────────────────────────────────

/// <summary>
/// Saves the studio's default pay terms (A07a). Saving is the owner reviewing them, like the hours form
/// (E05a, O02). The value the arrangement needs is required (a rate for Commission, a fee for
/// FlatFeePerSession), so a reviewed pay model always gives artists a share that can be worked out.
/// </summary>
public record UpdateStudioPayModelCommand(UpdateStudioPayModelRequest Request) : IRequest<StudioPayModelResponse>;

public class UpdateStudioPayModelValidator : AbstractValidator<UpdateStudioPayModelCommand>
{
    public UpdateStudioPayModelValidator()
    {
        RuleFor(c => c.Request.Arrangement)
            .Must(a => PayModelRules.TryParseArrangement(a, out _))
            .WithMessage("Arrangement must be Commission, BoothRent, FlatFeePerSession or Salaried.");
        RuleFor(c => c.Request.CommissionRate).ValidCommissionRate();
        RuleFor(c => c.Request.FlatFeePerSession).ValidFlatFee();
        RuleFor(c => c.Request.CommissionRate)
            .NotNull()
            .When(c => c.Request.Arrangement == nameof(ArtistPayArrangement.Commission))
            .WithMessage("Set the commission rate for commission.");
        RuleFor(c => c.Request.FlatFeePerSession)
            .NotNull()
            .When(c => c.Request.Arrangement == nameof(ArtistPayArrangement.FlatFeePerSession))
            .WithMessage("Set the fee per session for a flat fee.");
    }
}

public class UpdateStudioPayModelHandler(IAppDbContext db, ICurrentTenant tenant, TimeProvider time)
    : IRequestHandler<UpdateStudioPayModelCommand, StudioPayModelResponse>
{
    public async Task<StudioPayModelResponse> Handle(UpdateStudioPayModelCommand command, CancellationToken ct)
    {
        UpdateStudioPayModelRequest req = command.Request;
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        if (req.FlatFeePerSession is decimal fee && !CurrencyCatalog.HasAtMostMinorUnits(fee, studio.Currency))
            throw new BusinessRuleViolationException($"The fee has more decimal places than {studio.Currency} allows.");

        PayModelRules.TryParseArrangement(req.Arrangement, out ArtistPayArrangement arrangement);
        studio.DefaultPayArrangement = arrangement;
        studio.DefaultCommissionRate = req.CommissionRate;
        studio.DefaultFlatFeePerSession = req.FlatFeePerSession;
        studio.PayModelReviewedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return new StudioPayModelResponse(
            arrangement.ToString(), studio.DefaultCommissionRate, studio.DefaultFlatFeePerSession,
            studio.PayModelReviewedAt, studio.Currency);
    }
}

// ── POST /studios/me/pay-model/confirm (OwnerOnly) ───────────────────────────

/// <summary>
/// The owner says the pay terms already set are right (A07a), mirroring ConfirmStudioHoursCommand. Like
/// that command refusing a studio with no hours, this refuses terms that can't give a share (Commission
/// with no rate, a flat fee with no fee): confirming those would mark setup done while every commission
/// artist's earnings stayed unknown. Registration's default (Commission, no rate) therefore can't be
/// confirmed as is; the owner sets a rate through the form.
/// </summary>
public record ConfirmStudioPayModelCommand : IRequest;

public class ConfirmStudioPayModelValidator : AbstractValidator<ConfirmStudioPayModelCommand>
{
    // No input: the studio comes from the caller's tenant.
}

public class ConfirmStudioPayModelHandler(IAppDbContext db, ICurrentTenant tenant, TimeProvider time)
    : IRequestHandler<ConfirmStudioPayModelCommand>
{
    public async Task Handle(ConfirmStudioPayModelCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        ArtistShareResult probe = ArtistShare.Compute(
            1m, studio.DefaultPayArrangement, studio.DefaultCommissionRate, studio.DefaultFlatFeePerSession, studio.Currency);
        if (!probe.IsKnown)
            throw new BusinessRuleViolationException(
                studio.DefaultPayArrangement == ArtistPayArrangement.Commission
                    ? "Set the commission rate first."
                    : "Set the fee per session first.");

        // Idempotent: confirming again keeps the first confirmation rather than moving it.
        studio.PayModelReviewedAt ??= time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
