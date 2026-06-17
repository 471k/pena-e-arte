using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Models;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Services;

public class PortableProfileService(AppDbContext db) : IPortableProfileService
{
    public async Task<PortableClientProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct)
    {
        // Approved exception #3: portable profiles — see architecture.md Self-Promotion Module Architecture
        ClientProfile? profile = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Include(cp => cp.Client)
            .FirstOrDefaultAsync(
                cp => cp.Client.UserId == userId && cp.AllowCrossTenantRead && cp.DeletedAt == null,
                ct);

        if (profile is null) return null;

        return new PortableClientProfile(
            DisplayName:      $"{profile.Client.FirstName} {profile.Client.LastName[..1]}.",
            BodyMapLocations: profile.BodyMap.Locations,
            TattooHistory:    []);
    }

    public async Task<IReadOnlyList<PortableTattooRecord>> GetHistoryAsync(Guid userId, CancellationToken ct)
    {
        // Approved exception #3: portable profiles — see architecture.md Self-Promotion Module Architecture
        List<TattooRecord> records = await db.TattooRecords
            .IgnoreQueryFilters()
            .Include(t => t.Artist)
            .Include(t => t.Client)
            .Where(t => t.Client.UserId == userId
                     && t.DeletedAt == null
                     && db.ClientProfiles
                           .IgnoreQueryFilters()
                           .Any(cp => cp.ClientId == t.ClientId
                                   && cp.AllowCrossTenantRead
                                   && cp.DeletedAt == null))
            .OrderByDescending(t => t.CompletedAt)
            .ToListAsync(ct);

        return records.Select(r => new PortableTattooRecord(
            BodyLocation:   r.BodyLocation,
            PhotoUrls:      r.PhotoUrls,
            Description:    r.Description,
            CompletedAt:    r.CompletedAt,
            ArtistFirstName: r.Artist.FirstName)).ToList();
    }
}
