using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// Reads the facts DEC03 readiness is derived from and maps the result for clients. Shared by
/// GET /appointments/mine (summary counts per row) and GET /appointments/mine/{id} (itemised), so the
/// two can never disagree about the same appointment.
/// </summary>
internal static class AppointmentReadinessReader
{
    private sealed record ReadinessRow(Guid AppointmentId, ReadinessFacts Facts);

    /// <summary>
    /// The facts for every appointment in <paramref name="appointments"/>, in ONE SQL statement however
    /// many rows there are: each fact is a correlated EXISTS or scalar subquery in the same SELECT, and
    /// the service flags come from a single join. The bookings list therefore costs the same number of
    /// queries for one appointment as for a hundred (no N+1). Tenant query filters apply to every
    /// subquery as usual.
    /// </summary>
    public static async Task<Dictionary<Guid, ReadinessFacts>> LoadAsync(
        IAppDbContext db, IQueryable<Appointment> appointments, CancellationToken ct)
    {
        List<ReadinessRow> rows = await appointments
            .Select(a => new ReadinessRow(
                a.Id,
                new ReadinessFacts(
                    a.Service != null && a.Service.RequiresDepositPaid,
                    a.Service != null && a.Service.RequiresIntakeForm,
                    a.Service != null && a.Service.RequiresConsentForm,
                    a.Service != null && a.Service.RequiresReferenceImage,
                    a.Status,
                    a.DepositStatus,
                    a.DepositAmount,
                    // One deposit payment per appointment, looked up the way every payment read does.
                    db.Payments
                        .Where(p => p.AppointmentId == a.Id)
                        .Select(p => (PaymentStatus?)p.Status)
                        .FirstOrDefault(),
                    db.ConsentForms.Any(c => c.AppointmentId == a.Id && c.SignedAt != null),
                    db.IntakeForms.Any(i => i.AppointmentId == a.Id && i.SubmittedAt != null),
                    a.Attachments.Any(x => x.Category == AppointmentAttachmentCategory.Reference))))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.AppointmentId, r => r.Facts);
    }

    public static AppointmentReadinessSummaryResponse Summarize(IReadOnlyList<ReadinessItem> items) =>
        new(items.Count, items.Count(i => i.Satisfied));

    public static List<AppointmentReadinessItemResponse> Itemise(
        IReadOnlyList<ReadinessItem> items, ReadinessFacts facts, Guid appointmentId) =>
        items.Select(i => ToResponse(i, facts, appointmentId)).ToList();

    private static AppointmentReadinessItemResponse ToResponse(ReadinessItem item, ReadinessFacts facts, Guid appointmentId)
    {
        // Destinations are existing routes only; the form pages pre-select the appointment from the query string.
        (string label, string actionLabel, string? destination, string? note) = item.Requirement switch
        {
            ReadinessRequirement.DepositPaid => (
                "Deposit",
                "Pay your deposit",
                (string?)null, // paid on the appointment page itself
                facts.PaymentStatus == PaymentStatus.CashPending
                    ? "You chose to pay the deposit in cash at the studio."
                    : null),
            ReadinessRequirement.IntakeForm => (
                "Intake form",
                "Fill in the intake form",
                $"/forms/intake/new?appointmentId={appointmentId}",
                null),
            ReadinessRequirement.ConsentForm => (
                "Consent form",
                "Sign the consent form",
                $"/forms/consent/new?appointmentId={appointmentId}",
                null),
            ReadinessRequirement.ReferenceImage => (
                "Reference image",
                "Share a reference image",
                null,
                "Reference images are added when you book, so one can't be added here. Contact the studio about it."),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Requirement, null),
        };

        return new AppointmentReadinessItemResponse(
            item.Requirement.ToString(),
            label,
            actionLabel,
            Required: true,
            item.Satisfied,
            item.Available,
            item.Available ? destination : null,
            item.Satisfied ? null : note);
    }
}
