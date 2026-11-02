namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// What cancelling this appointment would do with its deposit, worked out by the server with the same
/// function DELETE /api/v1/appointments/{id} uses. A projection: the cancellation decides again when it
/// is committed (the studio's rule or the payment may change in between), and the committed result is
/// what counts.
/// </summary>
/// <param name="NoticeWindowHours">The notice window that applies (the studio's rule, or the platform default).</param>
/// <param name="IsWithinNoticeWindow">True when there is still at least that much notice before the appointment.</param>
/// <param name="NoticeWindowApplies">False for a staff caller: staff cancellations always refund in full.</param>
/// <param name="RefundPercent">The share of the deposit the policy refunds (100 for staff).</param>
/// <param name="DepositAmount">The appointment's deposit.</param>
/// <param name="Currency">The payment's currency, or the studio's when there is no payment.</param>
/// <param name="RefundAmount">What would go back to the client.</param>
/// <param name="KeptAmount">What the studio would keep of the money already taken.</param>
/// <param name="Outcome">NothingCollected, FullyRefunded, PartiallyRefunded, DepositKept, SettledWithStudio or Unknown.</param>
/// <param name="IsProjection">Always true: the server re-evaluates at commit.</param>
public record CancellationPreviewResponse(
    Guid AppointmentId,
    int NoticeWindowHours,
    bool IsWithinNoticeWindow,
    bool NoticeWindowApplies,
    int RefundPercent,
    decimal DepositAmount,
    string Currency,
    decimal RefundAmount,
    decimal KeptAmount,
    string Outcome,
    bool IsProjection = true);
