using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Queries;

public record GetPlanUsageQuery : IRequest<PlanUsageResponse?>;

public class GetPlanUsageHandler(IPlanLimitService planLimits)
    : IRequestHandler<GetPlanUsageQuery, PlanUsageResponse?>
{
    public async Task<PlanUsageResponse?> Handle(GetPlanUsageQuery query, CancellationToken ct)
    {
        PlanUsageSnapshot? snapshot = await planLimits.GetUsageSnapshotAsync(ct);
        return snapshot is null ? null : Map(snapshot);
    }

    private static PlanUsageResponse Map(PlanUsageSnapshot s) => new(
        s.PlanName,
        Map(s.Artists),
        Map(s.AppointmentsPerMonth),
        Map(s.NotificationsPerMonth),
        Map(s.StorageGb),
        Map(s.Locations));

    private static PlanUsageDimensionResponse Map(PlanUsageDimension d) => new(d.Current, d.Max);
}
