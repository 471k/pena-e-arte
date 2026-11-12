using System.Linq.Expressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Payments.Queries;

/// <summary>
/// GET /payments/unsettled (OwnerOnly): what the reconciliation job hasn't settled (E05b-2, O06).
/// <para>
/// PaymentReconciliationJob is the source of truth for card payment state: it asks the provider before
/// changing anything, daily and on every webhook. This query decides nothing. It lists the rows in the job's
/// own scope (card, with a provider order, still Pending or held) and shows what the job last recorded about
/// each: <see cref="Payment.ProviderCheckedAt"/> and <see cref="Payment.ProviderStatusAtCheck"/>.
/// </para>
/// Groups (each row is in exactly one):
/// <list type="bullet">
/// <item><b>ProviderDisagrees</b>: the provider's last answer is not the one the local status expects (Pending
/// expects Pending, held expects Authorized), and the job left it so. After a run the job has already moved
/// every row whose answer it acts on, so what remains is what it deliberately leaves for a person.</item>
/// <item><b>OnHold</b>: held, and the provider still said Authorized (or hasn't been asked yet).</item>
/// <item><b>AwaitingCardPayment</b>: unpaid, and the provider still said Pending (or hasn't been asked yet),
/// with the deadline after which the job cancels it, from <see cref="PaymentReleaseRule"/>.</item>
/// </list>
/// Cash promised and not yet confirmed is deliberately absent: the dashboard's CashToConfirm group owns that
/// count, and the screen links to it rather than counting it a second time.
/// <para>Cost: a count per group plus one bounded read for each group that has rows: at most six statements,
/// whatever the number of rows. The integration suite pins it.</para>
/// </summary>
public record GetUnsettledPaymentsQuery : IRequest<UnsettledPaymentsResponse>;

public class GetUnsettledPaymentsValidator : AbstractValidator<GetUnsettledPaymentsQuery>
{
    // No input: the studio comes from the caller's tenant and the time from the server's clock.
}

public class GetUnsettledPaymentsHandler(IAppDbContext db, TimeProvider time)
    : IRequestHandler<GetUnsettledPaymentsQuery, UnsettledPaymentsResponse>
{
    public const int MaxItemsPerGroup = 10;

    /// <summary>
    /// How long the provider may go without answering about a row before the row says its check is overdue.
    /// The job runs every day at 02:00 UTC (and on every webhook), so a healthy row is never more than about
    /// 24 h past its last answer; 26 h allows for a slow run. Beyond that, the daily run missed it or the
    /// provider call failed (a failed call leaves ProviderCheckedAt unchanged on purpose). A row the job has
    /// never asked about counts from when it was created.
    /// </summary>
    public static readonly TimeSpan CheckOverdueAfter = TimeSpan.FromHours(26);

    private sealed record Row(
        Guid PaymentId, Guid AppointmentId, DateTime AppointmentDate, AppointmentStatus AppointmentStatus,
        string ClientName, decimal Amount, string Currency, PaymentStatus Status, DateTime CreatedAt,
        DateTime? HoldExpiresAt, DateTime? ProviderCheckedAt, PaymentProviderStatus? ProviderStatusAtCheck);

    // The job's scope, exactly: ReconcilePendingAsync and ReconcileHeldAsync read card payments with a
    // provider order that are Pending or Captured (held).
    private static readonly Expression<Func<Payment, bool>> Disagrees = p =>
        p.Method == ClientPaymentMethod.Card && p.ProviderReferenceId != null && p.ProviderCheckedAt != null
        && ((p.Status == PaymentStatus.Pending && p.ProviderStatusAtCheck != PaymentProviderStatus.Pending)
            || (p.Status == PaymentStatus.Captured && p.ProviderStatusAtCheck != PaymentProviderStatus.Authorized));

    private static readonly Expression<Func<Payment, bool>> OnHold = p =>
        p.Method == ClientPaymentMethod.Card && p.ProviderReferenceId != null
        && p.Status == PaymentStatus.Captured
        && (p.ProviderCheckedAt == null || p.ProviderStatusAtCheck == PaymentProviderStatus.Authorized);

    private static readonly Expression<Func<Payment, bool>> Awaiting = p =>
        p.Method == ClientPaymentMethod.Card && p.ProviderReferenceId != null
        && p.Status == PaymentStatus.Pending
        && (p.ProviderCheckedAt == null || p.ProviderStatusAtCheck == PaymentProviderStatus.Pending);

    public async Task<UnsettledPaymentsResponse> Handle(GetUnsettledPaymentsQuery query, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;

        List<UnsettledPaymentGroupResponse> groups = [];
        foreach ((string kind, Expression<Func<Payment, bool>> predicate) in new[]
                 {
                     (UnsettledPaymentKinds.ProviderDisagrees, Disagrees),
                     (UnsettledPaymentKinds.OnHold, OnHold),
                     (UnsettledPaymentKinds.AwaitingCardPayment, Awaiting),
                 })
        {
            IQueryable<Payment> rows = db.Payments.Where(predicate);
            int total = await rows.CountAsync(ct);
            if (total == 0)
                continue; // a group appears only when it has rows; its read is skipped too

            List<Row> items = await rows
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
                .Take(MaxItemsPerGroup)
                .Select(p => new Row(
                    p.Id, p.AppointmentId, p.Appointment.Date, p.Appointment.Status,
                    p.Client.FirstName + " " + p.Client.LastName,
                    p.Amount, p.Currency, p.Status, p.CreatedAt, p.HoldExpiresAt,
                    p.ProviderCheckedAt, p.ProviderStatusAtCheck))
                .ToListAsync(ct);

            groups.Add(new UnsettledPaymentGroupResponse(
                kind, total, items.Select(r => Item(r, kind, now)).ToList()));
        }

        return new UnsettledPaymentsResponse(now, (int)CheckOverdueAfter.TotalHours, MaxItemsPerGroup, groups);
    }

    private static UnsettledPaymentItemResponse Item(Row r, string kind, DateTime now) =>
        new(
            r.PaymentId,
            r.AppointmentId,
            r.AppointmentDate,
            r.AppointmentStatus.ToString(),
            r.ClientName.Trim(),
            r.Amount,
            r.Currency,
            r.Status.ToString(),
            r.CreatedAt,
            kind == UnsettledPaymentKinds.AwaitingCardPayment
                ? PaymentReleaseRule.DeadlineFor(r.HoldExpiresAt, r.AppointmentDate)
                : null,
            r.ProviderCheckedAt,
            r.ProviderStatusAtCheck?.ToString(),
            (r.ProviderCheckedAt ?? r.CreatedAt) < now - CheckOverdueAfter);
}
