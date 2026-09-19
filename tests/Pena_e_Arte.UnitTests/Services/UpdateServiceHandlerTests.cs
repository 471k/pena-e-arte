using FluentAssertions;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class UpdateServiceHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private UpdateServiceHandler CreateSut() => new(_db);

    private Service Seed()
    {
        Service service = new()
        {
            StudioId = _studioId,
            Name = "Original",
            DurationMinutes = 60,
            Price = 100m,
            DepositAmount = 20m,
            IsActive = true,
        };
        _db.Services.Add(service);
        _db.SaveChanges();
        return service;
    }

    [Fact]
    public async Task Handle_ExistingService_UpdatesAllFields()
    {
        Service service = Seed();
        UpdateServiceRequest req = new("Renamed", "New description", 45, 200m, null, false);

        ServiceResponse result = await CreateSut().Handle(new UpdateServiceCommand(service.Id, req), default);

        result.Name.Should().Be("Renamed");
        result.Description.Should().Be("New description");
        result.DurationMinutes.Should().Be(45);
        result.Price.Should().Be(200m);
        result.DepositAmount.Should().BeNull();
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExistingService_PersistsChangesToDb()
    {
        Service service = Seed();
        UpdateServiceRequest req = new("Renamed", null, 45, null, null, false);

        await CreateSut().Handle(new UpdateServiceCommand(service.Id, req), default);

        _db.Services.Single(s => s.Id == service.Id).Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        UpdateServiceRequest req = new("Renamed", null, 45, null, null, false);

        Func<Task> act = () => CreateSut().Handle(new UpdateServiceCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
