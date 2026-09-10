using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Queries;

/// <summary>Owner-only CSV export of the studio's full client roster. Standard tenant-scoped
/// read — Client is a TenantEntity, the global query filter already scopes this; no
/// IgnoreQueryFilters(). No date range — a client "belongs to" the studio indefinitely.</summary>
public record ExportClientsCsvQuery : IRequest<string>;

public class ExportClientsCsvHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ExportClientsCsvQuery, string>
{
    public async Task<string> Handle(ExportClientsCsvQuery query, CancellationToken ct)
    {
        StringBuilder sb = new();
        sb.Append(CsvUtils.Bom);
        CsvUtils.AppendRow(sb, "First Name", "Last Name", "Email", "Phone", "Assigned Artist",
            "Client Since", "Marketing Opt-In");

        IQueryable<Client> clientsQuery = db.Clients
            .AsNoTracking()
            .Include(c => c.Artist)
            .Where(c => c.StudioId == tenant.StudioId)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName);

        await foreach (Client c in clientsQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            CsvUtils.AppendRow(sb,
                c.FirstName, c.LastName, c.Email, c.Phone,
                c.Artist is null ? "" : $"{c.Artist.FirstName} {c.Artist.LastName}",
                c.CreatedAt.ToString("yyyy-MM-dd"),
                c.MarketingOptIn ? "Yes" : "No");
        }

        return sb.ToString();
    }
}
