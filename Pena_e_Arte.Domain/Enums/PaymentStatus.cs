namespace Pena_e_Arte.Domain.Enums;

public enum PaymentStatus
{
    /// <summary>Card payment intent created, awaiting client action.</summary>
    Pending,

    /// <summary>Client selected cash; awaiting owner confirmation of receipt.</summary>
    CashPending,

    /// <summary>Card deposit authorised (held), not yet captured.</summary>
    Captured,

    /// <summary>Payment fully received — card captured or cash confirmed.</summary>
    Paid,

    /// <summary>Payment refunded.</summary>
    Refunded,

    /// <summary>Card payment failed.</summary>
    Failed,
}
