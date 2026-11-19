using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

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

public class GetAuditLogHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<GetAuditLogQuery, AuditLogPageResponse>
{
    /// <summary>Roles whose actor is named. Clients stay "Client" + id: an admin reads this log across every
    /// tenant, and a client's name is the most sensitive identity that could appear in it (Desktop, 19 Nov).</summary>
    internal static readonly IReadOnlySet<string> NamedRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "owner", "artist", "admin", "admin-impersonating" };

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
        // query filter, so this is an ordinary read. Staff actors are named below from Identity (never an email).
        List<AuditLogEntryResponse> items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogEntryResponse(
                a.Id, a.ActorUserId, a.ActorRole, a.Action, a.TargetType,
                a.TargetId, a.StudioId, a.Metadata, a.CreatedAt,
                db.Studios.Where(s => s.Id == a.StudioId).Select(s => s.Name).FirstOrDefault()))
            .ToListAsync(ct);

        // Phi's decision (19 Nov): the actor's CURRENT name, looked up now, not a copy stored with the entry. The id stays
        // the record; the name is a label on it. Only the page's distinct staff ids are looked up.
        List<Guid> staffIds = items.Where(i => NamedRoles.Contains(i.ActorRole)).Select(i => i.ActorUserId).Distinct().ToList();
        IReadOnlyDictionary<Guid, string?> names = await identity.GetAccountGivenNamesAsync(staffIds, ct);

        List<AuditLogEntryResponse> named = items
            .Select(i => !NamedRoles.Contains(i.ActorRole)
                ? i
                : names.TryGetValue(i.ActorUserId, out string? name)
                    ? i with { ActorName = string.IsNullOrWhiteSpace(name) ? null : name }
                    : i with { ActorAccountDeleted = true })
            .ToList();

        return new AuditLogPageResponse(named, totalCount, page, pageSize);
    }
}
