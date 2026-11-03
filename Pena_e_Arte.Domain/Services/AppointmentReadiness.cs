using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// One preparation requirement a studio can turn on per <see cref="Entities.Service"/> (DEC03).
/// Declared in priority order: when several are outstanding, the client's next action is the first
/// one here that they can act on.
/// </summary>
public enum ReadinessRequirement
{
    DepositPaid,
    IntakeForm,
    ConsentForm,
    ReferenceImage,
}

/// <summary>
/// The raw facts readiness is derived from, read in one query (see the Application layer's
/// AppointmentReadinessReader). Nothing here is stored; every fact already exists elsewhere.
/// </summary>
public sealed record ReadinessFacts(
    bool RequiresDepositPaid,
    bool RequiresIntakeForm,
    bool RequiresConsentForm,
    bool RequiresReferenceImage,
    AppointmentStatus Status,
    DepositStatus DepositStatus,
    decimal DepositAmount,
    PaymentStatus? PaymentStatus,
    bool ConsentSigned,
    bool IntakeSubmitted,
    bool HasReferenceImage);

/// <summary>
/// <paramref name="Available"/>: the client can do this in the app right now. Always false once
/// satisfied, and false for a requirement the app has no client path for.
/// </summary>
public sealed record ReadinessItem(ReadinessRequirement Requirement, bool Satisfied, bool Available);

/// <summary>
/// DEC03: whether an appointment is ready, derived at read time and never stored. Readiness is
/// advisory only. It never blocks booking or confirmation, because staff must be able to complete
/// a form with the client on the day.
/// </summary>
public static class AppointmentReadiness
{
    /// <summary>
    /// The requirements the appointment's service turns on, in priority order, each with whether
    /// it is met. Empty when the service configures none (or there is no service). That is an
    /// empty set, not "0 of 0", so the client sees no checklist at all.
    /// </summary>
    public static IReadOnlyList<ReadinessItem> Derive(ReadinessFacts f)
    {
        bool active = f.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;
        List<ReadinessItem> items = [];

        if (f.RequiresDepositPaid)
        {
            bool paid = IsDepositSecured(f.DepositStatus, f.DepositAmount, f.PaymentStatus);
            // A client who chose cash has nothing left to do in the app until they pay at the studio;
            // the bookings list still offers "Pay by card instead".
            bool canPayNow = active && f.PaymentStatus != Enums.PaymentStatus.CashPending;
            items.Add(new ReadinessItem(ReadinessRequirement.DepositPaid, paid, !paid && canPayNow));
        }

        if (f.RequiresIntakeForm)
            items.Add(new ReadinessItem(ReadinessRequirement.IntakeForm, f.IntakeSubmitted, !f.IntakeSubmitted && active));

        if (f.RequiresConsentForm)
            items.Add(new ReadinessItem(ReadinessRequirement.ConsentForm, f.ConsentSigned, !f.ConsentSigned && active));

        // Reference images are attached when booking; there is no client path to add one afterwards,
        // so an outstanding one is never offered as an action.
        if (f.RequiresReferenceImage)
            items.Add(new ReadinessItem(ReadinessRequirement.ReferenceImage, f.HasReferenceImage, false));

        return items;
    }

    /// <summary>
    /// The deposit is secured when the money is in (<see cref="DepositStatus.Paid"/>, set only when a
    /// card charge or the studio's cash confirmation lands), when a package covers the booking
    /// (<see cref="DepositStatus.PrePaid"/>), when nothing is due, or when a card hold is authorised
    /// (<see cref="Enums.PaymentStatus.Captured"/>, which the bookings list already shows the client as
    /// settled: there is nothing more for them to do). A <see cref="Enums.PaymentStatus.CashPending"/>
    /// declaration is a promise, not money received, so it does not count.
    /// </summary>
    public static bool IsDepositSecured(DepositStatus depositStatus, decimal depositAmount, PaymentStatus? paymentStatus) =>
        depositStatus is DepositStatus.Paid or DepositStatus.PrePaid
        || depositAmount <= 0m
        || paymentStatus is Enums.PaymentStatus.Captured or Enums.PaymentStatus.Paid;
}
