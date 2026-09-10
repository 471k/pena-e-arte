using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Support.Queries;

/// <summary>Admin-facing oversight list — every impersonation session across every
/// studio, newest first. Same pagination shape as GetAuditLogQuery.</summary>
public record GetImpersonationSessionsQuery(
    Guid? StudioId = null,
    int Page = 1,
    int PageSize = 20)
    : IRequest<ImpersonationSessionPageResponse>;

public class GetImpersonationSessionsHandler(IAppDbContext db)
    : IRequestHandler<GetImpersonationSessionsQuery, ImpersonationSessionPageResponse>
{
    public async Task<ImpersonationSessionPageResponse> Handle(GetImpersonationSessionsQuery query, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #52 — see StartImpersonationCommand /
        // EndImpersonationSessionCommand. The calling admin has no tenant_id claim while
        // browsing this cross-tenant oversight list, same shape as GetAuditLogQuery.
        IQueryable<ImpersonationSession> q = db.ImpersonationSessions.AsNoTracking().IgnoreQueryFilters();

        if (query.StudioId is Guid studioId)
            q = q.Where(s => s.StudioId == studioId);

        int totalCount = await q.CountAsync(ct);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        List<ImpersonationSessionResponse> items = await q
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ImpersonationSessionResponse(
                s.Id, s.ActorUserId, s.StudioId, s.ReasonCode, s.CreatedAt, s.ExpiresAt, s.EndedAt))
            .ToListAsync(ct);

        return new ImpersonationSessionPageResponse(items, totalCount, page, pageSize);
    }
}
