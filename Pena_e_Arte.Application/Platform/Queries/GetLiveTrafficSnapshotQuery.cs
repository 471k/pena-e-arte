using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// On-demand read for the initial page load, before the first TrafficSnapshotUpdated SignalR
/// push arrives — reads the same Redis structures as TrafficBroadcastService via the shared
/// ITrafficPresenceReader, so the two never disagree with each other.
/// </summary>
public record GetLiveTrafficSnapshotQuery : IRequest<LiveTrafficSnapshotResponse>;

public class GetLiveTrafficSnapshotHandler(ITrafficPresenceReader presenceReader)
    : IRequestHandler<GetLiveTrafficSnapshotQuery, LiveTrafficSnapshotResponse>
{
    public async Task<LiveTrafficSnapshotResponse> Handle(GetLiveTrafficSnapshotQuery query, CancellationToken ct)
    {
        TrafficPresenceSnapshot snapshot = await presenceReader.ReadSnapshotAsync(ct);
        return snapshot.ToResponse();
    }
}
