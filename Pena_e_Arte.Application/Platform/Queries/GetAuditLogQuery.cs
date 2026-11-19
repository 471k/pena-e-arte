using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Admin-facing cross-tenant audit log read. No IgnoreQueryFilters() needed — AuditLogEntry
/// has no query filter registered at all (see AppDbContext / architecture.md), so this is a
/// plain read across every studio's entries, not an approved-usages-table exception.
///
/// From and To are calendar DAYS in UTC, both inclusive (D01): To = 2026-11-14 covers that whole UTC day,
/// up to but not including 00:00 on the 15th. Only the date part of either value is used, so a caller can't
/// end a range at midnight by accident. The page says the times are UTC.
/// </summary>
public record GetAuditLogQuery(
    string? Action = null,
    string? TargetType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20,
    Guid? StudioId = null,
    Guid? ActorUserId = null)
    : IRequest<AuditLogPageResponse>;

public class GetAuditLogValidator : AbstractValidator<GetAuditLogQuery>
{
    public GetAuditLogValidator()
    {
        RuleFor(x => x.To!.Value.Date)
            .GreaterThanOrEqualTo(x => x.From!.Value.Date)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("'to' must not be before 'from'.");
        RuleFor(x => x.Action)
            .Must(a => AuditVocabulary.Actions.Contains(a!))
            .When(x => !string.IsNullOrWhiteSpace(x.Action))
            .WithMessage("Unknown audit action.");
        RuleFor(x => x.TargetType)
            .Must(t => AuditVocabulary.TargetTypes.Contains(t!))
            .When(x => !string.IsNullOrWhiteSpace(x.TargetType))
            .WithMessage("Unknown audit target type.");
    }
}

public class GetAuditLogHandler(IAppDbContext db)
    : IRequestHandler<GetAuditLogQuery, AuditLogPageResponse>
{
    public async Task<AuditLogPageResponse> Handle(GetAuditLogQuery query, CancellationToken ct)
    {
        IQueryable<AuditLogEntry> q = db.AuditLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.TargetType))
            q = q.Where(a => a.TargetType == query.TargetType);
        if (query.StudioId is Guid studioId)
            q = q.Where(a => a.StudioId == studioId);
        if (query.ActorUserId is Guid actorUserId)
            q = q.Where(a => a.ActorUserId == actorUserId);
        if (query.From is DateTime from)
        {
            DateTime fromDay = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            q = q.Where(a => a.CreatedAt >= fromDay);
        }
        if (query.To is DateTime to)
        {
            // Exclusive upper bound at the START of the next day, so the whole end day is included.
            DateTime dayAfterTo = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(a => a.CreatedAt < dayAfterTo);
        }

        int totalCount = await q.CountAsync(ct);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        // The studio's name is resolved here so a record reads without inside knowledge (D01). Studio has no
        // query filter, so this is an ordinary read. The actor stays an id + role: resolving a person's name
        // or email is a change to what this read exposes, and that is Phi's decision (slice 8 spec §8).
        List<AuditLogEntryResponse> items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogEntryResponse(
                a.Id, a.ActorUserId, a.ActorRole, a.Action, a.TargetType,
                a.TargetId, a.StudioId, a.Metadata, a.CreatedAt,
                db.Studios.Where(s => s.Id == a.StudioId).Select(s => s.Name).FirstOrDefault()))
            .ToListAsync(ct);

        return new AuditLogPageResponse(items, totalCount, page, pageSize);
    }
}
