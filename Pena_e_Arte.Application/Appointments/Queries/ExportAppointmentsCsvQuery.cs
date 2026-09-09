using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>Owner-only CSV export of the studio's appointments, optionally bounded by
/// Date (mirrors GetRevenueSummaryQuery's optional-range pattern). Standard tenant-scoped
/// read — Appointment is a TenantEntity, the global query filter already scopes this; no
/// IgnoreQueryFilters().</summary>
public record ExportAppointmentsCsvQuery(DateTime? From, DateTime? To) : IRequest<string>;

public class ExportAppointmentsCsvHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ExportAppointmentsCsvQuery, string>
{
    public async Task<string> Handle(ExportAppointmentsCsvQuery query, CancellationToken ct)
    {
        StringBuilder sb = new();
        sb.Append(CsvUtils.Bom);
        CsvUtils.AppendRow(sb, "Date", "End Time", "Duration (min)", "Artist", "Client",
            "Status", "Deposit Status", "Deposit Amount", "Cancellation Reason");

        IQueryable<Appointment> appointmentsQuery = db.Appointments
            .AsNoTracking()
            .Include(a => a.Artist)
            .Include(a => a.Client)
            .Where(a => a.StudioId == tenant.StudioId);

        if (query.From is DateTime from) appointmentsQuery = appointmentsQuery.Where(a => a.Date >= from);
        if (query.To is DateTime to) appointmentsQuery = appointmentsQuery.Where(a => a.Date <= to);

        appointmentsQuery = appointmentsQuery.OrderBy(a => a.Date);

        await foreach (Appointment a in appointmentsQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            CsvUtils.AppendRow(sb,
                a.Date.ToString("yyyy-MM-dd HH:mm"),
                a.EndDate.ToString("yyyy-MM-dd HH:mm"),
                a.DurationMinutes.ToString(),
                a.Artist is null ? "" : $"{a.Artist.FirstName} {a.Artist.LastName}",
                $"{a.Client.FirstName} {a.Client.LastName}",
                a.Status.ToString(),
                a.DepositStatus.ToString(),
                a.DepositAmount.ToString("F2"),
                a.CancellationReason?.ToString() ?? "");
        }

        return sb.ToString();
    }
}
