using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>
/// Seeds a studio that is open around the clock, every day, with one artist working all those hours, so a
/// test that moves an appointment isn't refused by the schedule check the reschedule save makes
/// (studio hours, artist hours; Phi, 2026-11-05). Tests about the schedule itself seed their own hours.
/// </summary>
internal static class OpenSchedule
{
    public static void Seed(FakeDbContext db, Guid studioId, Guid artistId)
    {
        TimeSpan endOfDay = new(23, 59, 0);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            db.StudioHours.Add(new StudioHours { StudioId = studioId, DayOfWeek = day, StartTime = TimeSpan.Zero, EndTime = endOfDay, IsOpen = true });
            db.ArtistSchedules.Add(new ArtistSchedule { StudioId = studioId, ArtistId = artistId, DayOfWeek = day, StartTime = TimeSpan.Zero, EndTime = endOfDay, IsAvailable = true });
        }
        db.SaveChanges();
    }
}
