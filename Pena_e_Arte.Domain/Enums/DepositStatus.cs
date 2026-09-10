namespace Pena_e_Arte.Domain.Enums;

public enum DepositStatus
{
    Pending,
    Paid,
    Forfeited,
    Refunded,

    /// <summary>Booking is covered by a prepaid Package session — no deposit was ever due.</summary>
    PrePaid
}
