using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reports.Queries;

/// <summary>Owner-only CSV export of the studio's payment-level revenue ledger — one row per
/// Payment, not the aggregated GetRevenueSummaryQuery view. Same Paid/Refunded inclusion rule
/// as GetRevenueSummaryHandler (a partially-refunded payment still retains money). Standard
/// tenant-scoped read — Payment is a TenantEntity, the global query filter already scopes
/// this; no IgnoreQueryFilters().</summary>
public record ExportRevenueCsvQuery(DateTime? From, DateTime? To) : IRequest<string>;

public class ExportRevenueCsvHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ExportRevenueCsvQuery, string>
{
    public async Task<string> Handle(ExportRevenueCsvQuery query, CancellationToken ct)
    {
        StringBuilder sb = new();
        sb.Append(CsvUtils.Bom);
        CsvUtils.AppendRow(sb, "Paid At", "Client", "Artist", "Appointment Date", "Amount",
            "Retained Amount", "Status", "Method", "Provider");

        IQueryable<Payment> paymentsQuery = db.Payments
            .AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Appointment).ThenInclude(a => a.Artist)
            .Where(p => p.StudioId == tenant.StudioId
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded)
                && p.PaidAt != null);

        if (query.From is DateTime from) paymentsQuery = paymentsQuery.Where(p => p.PaidAt >= from);
        if (query.To is DateTime to) paymentsQuery = paymentsQuery.Where(p => p.PaidAt <= to);

        paymentsQuery = paymentsQuery.OrderBy(p => p.PaidAt);

        await foreach (Payment p in paymentsQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            CsvUtils.AppendRow(sb,
                p.PaidAt!.Value.ToString("yyyy-MM-dd HH:mm"),
                $"{p.Client.FirstName} {p.Client.LastName}",
                p.Appointment.Artist is null ? "" : $"{p.Appointment.Artist.FirstName} {p.Appointment.Artist.LastName}",
                p.Appointment.Date.ToString("yyyy-MM-dd HH:mm"),
                p.Amount.ToString("F2"),
                p.RetainedAmount().ToString("F2"),
                p.Status.ToString(),
                p.Method.ToString(),
                p.Provider);
        }

        return sb.ToString();
    }
}
