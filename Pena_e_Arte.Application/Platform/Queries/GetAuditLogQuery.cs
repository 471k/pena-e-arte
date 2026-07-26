using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Issuer-facing cross-tenant audit log read. No IgnoreQueryFilters() needed — AuditLogEntry
/// has no query filter registered at all (see AppDbContext / architecture.md), so this is a
/// plain read across every studio's entries, not an approved-usages-table exception.
/// </summary>
public record GetAuditLogQuery(
    string? Action = null,
    string? TargetType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20)
    : IRequest<AuditLogPageResponse>;

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
        if (query.From is DateTime from)
            q = q.Where(a => a.CreatedAt >= from);
        if (query.To is DateTime to)
            q = q.Where(a => a.CreatedAt <= to);

        int totalCount = await q.CountAsync(ct);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        List<AuditLogEntryResponse> items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogEntryResponse(
                a.Id, a.ActorUserId, a.ActorRole, a.Action, a.TargetType,
                a.TargetId, a.StudioId, a.Metadata, a.CreatedAt))
            .ToListAsync(ct);

        return new AuditLogPageResponse(items, totalCount, page, pageSize);
    }
}
