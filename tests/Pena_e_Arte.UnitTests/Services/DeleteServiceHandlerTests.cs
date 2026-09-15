using FluentAssertions;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class DeleteServiceHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private DeleteServiceHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingService_SetsDeletedAt()
    {
        Service service = new() { StudioId = _studioId, Name = "Doomed", DurationMinutes = 30, IsActive = true };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteServiceCommand(service.Id), default);

        _db.Services.Single(s => s.Id == service.Id).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeleteServiceCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DeletedService_DoesNotDeleteAppointmentsReferencingIt()
    {
        Service service = new() { StudioId = _studioId, Name = "Doomed", DurationMinutes = 30, IsActive = true };
        _db.Services.Add(service);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            ServiceId = service.Id,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            DurationMinutes = 30,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteServiceCommand(service.Id), default);

        _db.Appointments.Should().ContainSingle(a => a.Id == appointment.Id);
    }
}
