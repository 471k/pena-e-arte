using FluentAssertions;
using Pena_e_Arte.Application.Reminders.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reminders;

public class GetManualRemindersHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetManualRemindersHandler CreateSut() => new(_db);

    private Guid SeedReminder(Guid? appointmentId, Guid? clientId)
    {
        Guid artistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = artistId, FirstName = "Jo", LastName = "Artist", Email = $"{Guid.NewGuid()}@a.com" });
        ManualReminder reminder = new()
        {
            StudioId = _studioId, ArtistId = artistId, AppointmentId = appointmentId, ClientId = clientId,
            RecipientName = "Walk-in", RecipientPhone = "+351900000000",
            ScheduledFor = DateTime.UtcNow.AddHours(1), Status = ManualReminderStatus.Scheduled
        };
        _db.ManualReminders.Add(reminder);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return reminder.Id;
    }

    [Fact]
    public async Task Handle_NoFilterProvided_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetManualRemindersQuery(null, null), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_FilterByAppointmentId_ReturnsMatchingReminders()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid id = SeedReminder(appointmentId, null);
        SeedReminder(Guid.NewGuid(), null);

        var result = await CreateSut().Handle(new GetManualRemindersQuery(appointmentId, null), default);

        result.Should().ContainSingle(r => r.Id == id);
    }

    [Fact]
    public async Task Handle_FilterByClientId_ReturnsMatchingReminders()
    {
        Guid clientId = Guid.NewGuid();
        Guid id = SeedReminder(null, clientId);
        SeedReminder(null, Guid.NewGuid());

        var result = await CreateSut().Handle(new GetManualRemindersQuery(null, clientId), default);

        result.Should().ContainSingle(r => r.Id == id);
    }
}
