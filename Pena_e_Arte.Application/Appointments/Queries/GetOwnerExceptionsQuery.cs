using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// GET /dashboard/exceptions (OwnerOnly): what needs the owner, for the owner's home (E05a, O01).
/// Every source already existed; this composes them with real counts instead of the dashboard counting
/// whatever one page of a list happened to return.
///
/// Groups (see <see cref="OwnerExceptionKinds"/>):
/// - Unassigned: Pending/Confirmed, from now on, no artist.
/// - AwaitingConfirmation: Pending with an artist, from now on (ConfirmAppointment refuses an unassigned
///   request, so the two never overlap).
/// - CashToConfirm: every CashPending deposit payment, whatever the appointment date, as the old
///   "Awaiting Cash" card listed them (but all of them, not those among the 50 oldest payments).
/// - UnpaidDeposit: Pending/Confirmed in the next <see cref="WindowDays"/> days whose deposit is due and
///   not secured by <see cref="AppointmentReadiness.IsDepositSecured"/>, excluding cash promises (those
///   are CashToConfirm).
/// - Preparation: Pending/Confirmed in the next <see cref="WindowDays"/> days with any DEC03 readiness
///   item outstanding, derived by <see cref="AppointmentReadiness.Derive"/>. A missing deposit on a
///   service that requires one appears here too, as one of the outstanding items.
///
/// Cost: a fixed number of SQL statements whatever the number of exceptions (count + soonest items per
/// group, one read of the window's rows, one readiness read via AppointmentReadinessReader, one read of
/// the shown unpaid deposits' payments and one of the studio's currency). The integration suite asserts it.
/// </summary>
public record GetOwnerExceptionsQuery : IRequest<OwnerExceptionsResponse>;

public class GetOwnerExceptionsValidator : AbstractValidator<GetOwnerExceptionsQuery>
{
    // No input: the studio comes from the caller's tenant and the time from the server's clock.
}

public class GetOwnerExceptionsHandler(IAppDbContext db, ICurrentTenant tenant, TimeProvider time)
    : IRequestHandler<GetOwnerExceptionsQuery, OwnerExceptionsResponse>
{
    /// <summary>The same 7 days as the dashboard's "This week".</summary>
    public const int WindowDays = 7;

    public const int MaxItemsPerGroup = 10;

    private sealed record Row(
        Guid Id, DateTime Date, string ClientName, string? ArtistName, AppointmentStatus Status, decimal DepositAmount);

    public async Task<OwnerExceptionsResponse> Handle(GetOwnerExceptionsQuery query, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateTime windowEnd = now.AddDays(WindowDays);

        IQueryable<Appointment> upcoming = db.Appointments
            .Where(a => (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                        && a.Date >= now);

        // ── Unassigned and awaiting confirmation: count + soonest items ──────────
        IQueryable<Appointment> unassigned = upcoming.Where(a => a.ArtistId == null);
        IQueryable<Appointment> awaiting = upcoming.Where(a => a.Status == AppointmentStatus.Pending && a.ArtistId != null);

        int unassignedTotal = await unassigned.CountAsync(ct);
        List<Row> unassignedRows = await Project(unassigned.OrderBy(a => a.Date).Take(MaxItemsPerGroup)).ToListAsync(ct);
        int awaitingTotal = await awaiting.CountAsync(ct);
        List<Row> awaitingRows = await Project(awaiting.OrderBy(a => a.Date).Take(MaxItemsPerGroup)).ToListAsync(ct);

        // ── Cash promised, not yet confirmed (any date) ──────────────────────────
        IQueryable<Payment> cash = db.Payments.Where(p => p.Status == PaymentStatus.CashPending);
        int cashTotal = await cash.CountAsync(ct);
        List<Payment> cashPayments = await cash
            .Include(p => p.Client)
            .Include(p => p.Appointment).ThenInclude(a => a.Artist)
            .OrderBy(p => p.Appointment.Date)
            .Take(MaxItemsPerGroup)
            .ToListAsync(ct);

        // ── The next WindowDays: unpaid deposits and preparation ─────────────────
        IQueryable<Appointment> window = upcoming.Where(a => a.Date < windowEnd);
        List<Row> windowRows = await Project(window.OrderBy(a => a.Date)).ToListAsync(ct);
        Dictionary<Guid, ReadinessFacts> facts = await AppointmentReadinessReader.LoadAsync(db, window, ct);

        List<Row> unpaid = windowRows
            .Where(r => facts.TryGetValue(r.Id, out ReadinessFacts? f)
                        && f.DepositStatus == DepositStatus.Pending
                        && f.PaymentStatus != PaymentStatus.CashPending
                        && !AppointmentReadiness.IsDepositSecured(f.DepositStatus, f.DepositAmount, f.PaymentStatus))
            .ToList();
        List<(Row Row, AppointmentReadinessSummaryResponse Summary)> unprepared = windowRows
            .Select(r => (Row: r, Items: facts.TryGetValue(r.Id, out ReadinessFacts? f) ? AppointmentReadiness.Derive(f) : Array.Empty<ReadinessItem>()))
            .Where(x => x.Items.Any(i => !i.Satisfied))
            .Select(x => (x.Row, AppointmentReadinessReader.Summarize(x.Items)))
            .ToList();

        List<Guid> unpaidShown = unpaid.Take(MaxItemsPerGroup).Select(r => r.Id).ToList();
        Dictionary<Guid, Payment> unpaidPayments = (await db.Payments
                .Include(p => p.Client)
                .Include(p => p.Appointment)
                .Where(p => unpaidShown.Contains(p.AppointmentId))
                .ToListAsync(ct))
            // One deposit payment per appointment; should a second ever exist, show the latest.
            .GroupBy(p => p.AppointmentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());
        string currency = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => s.Currency)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        // ── Compose ───────────────────────────────────────────────────────────────
        List<OwnerExceptionGroupResponse> groups =
        [
            new(OwnerExceptionKinds.Unassigned, unassignedTotal, unassignedRows.Select(r => Item(r)).ToList()),
            new(OwnerExceptionKinds.AwaitingConfirmation, awaitingTotal, awaitingRows.Select(r => Item(r)).ToList()),
            new(OwnerExceptionKinds.CashToConfirm, cashTotal, cashPayments.Select(CashItem).ToList()),
            new(OwnerExceptionKinds.UnpaidDeposit, unpaid.Count, unpaid.Take(MaxItemsPerGroup)
                .Select(r => Item(r, deposit: new OwnerExceptionDepositResponse(
                    r.DepositAmount,
                    unpaidPayments.TryGetValue(r.Id, out Payment? p) ? p.Currency : currency,
                    p is null ? null : Sanitize(p))))
                .ToList()),
            new(OwnerExceptionKinds.Preparation, unprepared.Count, unprepared.Take(MaxItemsPerGroup)
                .Select(x => Item(x.Row, readiness: x.Summary))
                .ToList()),
        ];

        // Ordering rule (stated, not a judgement of commercial importance): the group whose soonest
        // appointment comes first leads, so anything overdue is at the top; equal dates keep the fixed
        // order of OwnerExceptionKinds.All. Empty groups are left out.
        List<OwnerExceptionGroupResponse> ordered = groups
            .Where(g => g.Total > 0 && g.Items.Count > 0)
            .OrderBy(g => g.Items[0].AppointmentDate)
            .ThenBy(g => OwnerExceptionKinds.All.ToList().IndexOf(g.Kind))
            .ToList();

        return new OwnerExceptionsResponse(now, WindowDays, MaxItemsPerGroup, ordered);
    }

    private static IQueryable<Row> Project(IQueryable<Appointment> q) =>
        q.Select(a => new Row(
            a.Id,
            a.Date,
            a.Client.FirstName + " " + a.Client.LastName,
            a.Artist != null ? a.Artist.FirstName + " " + a.Artist.LastName : null,
            a.Status,
            a.DepositAmount));

    private static OwnerExceptionItemResponse Item(
        Row r, AppointmentReadinessSummaryResponse? readiness = null, OwnerExceptionDepositResponse? deposit = null) =>
        new(r.Id, r.Date, r.ClientName, r.ArtistName, r.Status.ToString(), readiness, deposit);

    private static OwnerExceptionItemResponse CashItem(Payment p) =>
        new(
            p.AppointmentId,
            p.Appointment.Date,
            $"{p.Client.FirstName} {p.Client.LastName}".Trim(),
            p.Appointment.Artist is null ? null : $"{p.Appointment.Artist.FirstName} {p.Appointment.Artist.LastName}",
            p.Appointment.Status.ToString(),
            Deposit: new OwnerExceptionDepositResponse(p.Amount, p.Currency, Sanitize(p)));

    /// <summary>
    /// The payment as every deposit screen sees it, minus what the home screen has no use for: no
    /// provider reference or client token, and no session splits (earnings belong to A07, not the owner home).
    /// </summary>
    private static PaymentResponse Sanitize(Payment p) =>
        p.ToResponse([]) with { ProviderReferenceId = null, ClientToken = null, Splits = [] };
}
