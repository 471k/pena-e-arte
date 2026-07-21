using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Queries;

/// <summary>
/// Owner-facing read-only view of recent actions on their own studio. Explicitly filters
/// by StudioId == the caller's own tenant — AuditLogEntry has no query filter to rely on
/// (see architecture.md), so this handler is the only thing standing between an owner and
/// every other studio's audit rows. Never trust the absence of a global filter to do this
/// scoping automatically.
/// </summary>
public record GetMyStudioAuditLogQuery(
    string?   Action     = null,
    DateTime? From       = null,
    DateTime? To         = null,
    int       Page       = 1,
    int       PageSize   = 20)
    : IRequest<AuditLogPageResponse>;

public class GetMyStudioAuditLogHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetMyStudioAuditLogQuery, AuditLogPageResponse>
{
    public async Task<AuditLogPageResponse> Handle(GetMyStudioAuditLogQuery query, CancellationToken ct)
    {
        IQueryable<AuditLogEntry> q = db.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.StudioId == tenant.StudioId);

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);
        if (query.From is DateTime from)
            q = q.Where(a => a.CreatedAt >= from);
        if (query.To is DateTime to)
            q = q.Where(a => a.CreatedAt <= to);

        int totalCount = await q.CountAsync(ct);

        int page     = Math.Max(1, query.Page);
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
