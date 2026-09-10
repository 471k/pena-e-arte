using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily sweep: for every active BoothRentSchedule whose NextChargeDate has arrived, writes a
/// bookkeeping BoothRentCharge row and advances NextChargeDate — never calls IPaymentProvider
/// (booth rent is bookkeeping-only per the 2026-09-09 product decision; see architecture.md
/// Decisions Log). Cross-tenant, IgnoreQueryFilters — same shape as every other daily platform-
/// wide job (see RetentionPurgeJob).
/// </summary>
public class BoothRentChargeJob(IAppDbContext db, ILogger<BoothRentChargeJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;

        List<BoothRentSchedule> due = await db.BoothRentSchedules
            .IgnoreQueryFilters()
            .Where(s => s.IsActive && s.DeletedAt == null && s.NextChargeDate <= now)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        foreach (BoothRentSchedule schedule in due)
        {
            db.BoothRentCharges.Add(new BoothRentCharge
            {
                StudioId = schedule.StudioId,
                BoothRentScheduleId = schedule.Id,
                ArtistId = schedule.ArtistId,
                Amount = schedule.AmountFixed,
                ChargedDate = schedule.NextChargeDate,
                IsSettled = false,
            });

            schedule.NextChargeDate = schedule.Frequency == RentFrequency.Weekly
                ? schedule.NextChargeDate.AddDays(7)
                : schedule.NextChargeDate.AddMonths(1);
            schedule.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "BoothRentChargeJob created {Count} booth-rent charge(s) and advanced their schedules",
            due.Count);
    }
}
