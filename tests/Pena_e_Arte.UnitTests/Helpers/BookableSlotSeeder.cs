using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>
/// Makes an artist bookable at any hour of any day (studio open, artist scheduled, all day every day), so
/// availability-dependent tests don't depend on the time of day they run at. With no Studio row seeded, the
/// availability rules read the studio's timezone as UTC.
/// </summary>
public static class BookableSlotSeeder
{
    public static void SeedAlwaysBookableArtist(FakeDbContext db, Guid studioId, Guid artistId)
    {
        db.Artists.Add(new Artist
        {
            Id = artistId,
            StudioId = studioId,
            FirstName = "Bookable",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@artist.test",
            IsActive = true,
        });

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            db.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = studioId,
                ArtistId = artistId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            db.StudioHours.Add(new StudioHours
            {
                StudioId = studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsOpen = true,
            });
        }

        db.SaveChanges();
    }

    /// <summary>A future slot well inside the day: 10:00 UTC, three days from now.</summary>
    public static DateTime FutureSlot(int daysAhead = 3) => DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(10);

    /// <summary>A booked (non-cancelled) appointment occupying the slot.</summary>
    public static Appointment SeedAppointment(
        FakeDbContext db, Guid studioId, Guid? artistId, DateTime start, int durationMinutes = 60)
    {
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = start,
            EndDate = start.AddMinutes(durationMinutes),
            DurationMinutes = durationMinutes,
        };
        db.Appointments.Add(appointment);
        db.SaveChanges();
        return appointment;
    }
}
