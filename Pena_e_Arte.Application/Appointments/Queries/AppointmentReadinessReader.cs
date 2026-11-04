using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// Reads the facts DEC03 readiness is derived from and maps the result: for clients, and (E04a) for the
/// staff who prepare the session. Shared by GET /appointments/mine and the staff list GET /appointments
/// (summary counts per row), GET /appointments/mine/{id} (itemised for the client) and
/// GET /appointments/{id}/preparation (itemised for the artist), so none of them can disagree about the
/// same appointment. Only the wording differs between the two audiences.
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

    /// <summary>
    /// The same items worded for the artist preparing the session (E04a, A03): whether the client has
    /// done each thing, and for an outstanding one what the client can still do, so the artist knows what
    /// to ask for. Never the client's own instructions ("Sign the consent form"): an artist doesn't sign
    /// the client's consent form.
    /// </summary>
    public static List<AppointmentPreparationItemResponse> ItemiseForStaff(
        IReadOnlyList<ReadinessItem> items, ReadinessFacts facts) =>
        items.Select(i => ToStaffResponse(i, facts)).ToList();

    private static AppointmentPreparationItemResponse ToStaffResponse(ReadinessItem item, ReadinessFacts facts)
    {
        // Once the session is over (or cancelled) there is nothing left to follow up.
        bool upcoming = facts.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;
        bool cashPromised = facts.PaymentStatus == PaymentStatus.CashPending;

        (string label, string done, string outstanding, string? followUp) = item.Requirement switch
        {
            ReadinessRequirement.DepositPaid => (
                "Deposit",
                facts.DepositAmount <= 0m ? "Nothing due" : "Secured",
                cashPromised ? "Cash not received yet" : "Not paid yet",
                cashPromised
                    ? "The client chose to pay in cash at the studio."
                    : "The client can pay it from their appointment page."),
            ReadinessRequirement.IntakeForm => (
                "Intake form",
                "Submitted",
                "Not submitted yet",
                "The client can fill it in from their appointment page."),
            ReadinessRequirement.ConsentForm => (
                "Consent form",
                "Signed",
                "Not signed yet",
                "The client can sign it from their appointment page."),
            ReadinessRequirement.ReferenceImage => (
                "Reference image",
                "Added",
                "None added",
                "Clients can only add reference images when they book, so ask the client about it."),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Requirement, null),
        };

        return new AppointmentPreparationItemResponse(
            item.Requirement.ToString(),
            label,
            item.Satisfied,
            item.Satisfied ? done : outstanding,
            item.Satisfied || !upcoming ? null : followUp);
    }

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
