namespace Pena_e_Arte.Contracts.Responses;

/// <summary>One cash-billed subscription the backfill snapshotted from its plan's Monthly
/// price — listed so the admin can review what was assumed, since nothing is billed by
/// this action itself.</summary>
public record CashBilledSnapshotResponse(Guid StudioId, decimal Price);

public record BackfillSubscriptionBilledAmountsResponse(
    int CardBilledUpdated,
    int CardBilledSkipped,
    List<CashBilledSnapshotResponse> CashBilledSnapshots);
