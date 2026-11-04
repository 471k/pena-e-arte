using FluentAssertions;
using Pena_e_Arte.Application.Messaging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class MessageAppointmentLabelsTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private Guid SeedAppointment(DateTime startsAt, string? serviceName)
    {
        Client client = new() { StudioId = _studioId, UserId = Guid.NewGuid(), FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        _db.Clients.Add(client);
        Service? service = serviceName is null
            ? null
            : new Service { StudioId = _studioId, Name = serviceName, DurationMinutes = 60, IsActive = true };
        if (service is not null) _db.Services.Add(service);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = client.Id,
            ServiceId = service?.Id,
            Date = startsAt,
            EndDate = startsAt.AddHours(1),
            DurationMinutes = 60,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return appointment.Id;
    }

    [Fact]
    public void Build_WithAServiceName_KeepsIt()
    {
        Guid id = Guid.NewGuid();
        DateTime startsAt = new(2026, 12, 3, 14, 30, 0, DateTimeKind.Utc);

        MessageAppointmentLabelResponse label = MessageAppointmentLabels.Build(id, startsAt, "Fine line session");

        label.Should().Be(new MessageAppointmentLabelResponse(id, startsAt, "Fine line session"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithoutAServiceName_FallsBackToTheDateAlone(string? serviceName)
    {
        MessageAppointmentLabelResponse label = MessageAppointmentLabels.Build(Guid.NewGuid(), DateTime.UtcNow, serviceName);

        label.ServiceName.Should().BeNull("an empty segment must never be rendered, nor the word null");
    }

    [Fact]
    public void For_AMessageWithNoAppointment_IsNull_NotAnEmptyLabel()
    {
        Dictionary<Guid, MessageAppointmentLabelResponse> labels = new()
        {
            [Guid.NewGuid()] = MessageAppointmentLabels.Build(Guid.NewGuid(), DateTime.UtcNow, "x"),
        };

        MessageAppointmentLabels.For(labels, null).Should().BeNull();
        MessageAppointmentLabels.For(labels, Guid.NewGuid()).Should().BeNull("an id that no longer resolves gets no label");
    }

    [Fact]
    public async Task ResolveAsync_ReadsDateAndServiceFromTheAppointment()
    {
        DateTime withServiceAt = new(2026, 12, 3, 14, 30, 0, DateTimeKind.Utc);
        DateTime withoutServiceAt = new(2026, 12, 10, 9, 0, 0, DateTimeKind.Utc);
        Guid withService = SeedAppointment(withServiceAt, "Touch-up");
        Guid withoutService = SeedAppointment(withoutServiceAt, null);

        Dictionary<Guid, MessageAppointmentLabelResponse> labels = await MessageAppointmentLabels.ResolveAsync(
            _db, [withService, null, withoutService, withService], default);

        labels.Should().HaveCount(2);
        labels[withService].Should().Be(new MessageAppointmentLabelResponse(withService, withServiceAt, "Touch-up"));
        labels[withoutService].Should().Be(new MessageAppointmentLabelResponse(withoutService, withoutServiceAt, null));
    }

    [Fact]
    public async Task ResolveAsync_WithNoIds_ReturnsEmpty()
    {
        Dictionary<Guid, MessageAppointmentLabelResponse> labels =
            await MessageAppointmentLabels.ResolveAsync(_db, [null, null], default);

        labels.Should().BeEmpty();
    }
}
