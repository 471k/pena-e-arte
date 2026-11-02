namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// What a cancellation asks the payment provider to do with the deposit — decided by
/// <see cref="Services.CancellationOutcome"/>, performed by CancelAppointmentHandler.
/// </summary>
public enum CancellationProviderAction
{
    /// <summary>No provider call (no card money, or a charged deposit the studio keeps as it is).</summary>
    None,

    /// <summary>Release an authorised hold: the full deposit goes back, nothing is charged.</summary>
    ReleaseHold,

    /// <summary>Capture the whole authorised hold: the studio keeps the full deposit.</summary>
    CaptureWhole,

    /// <summary>Capture only the share the studio keeps; the provider releases the rest.</summary>
    CaptureKeptShare,

    /// <summary>Refund (all or part of) an already-charged deposit.</summary>
    Refund,
}
