using FluentAssertions;
using Pena_e_Arte.Application.Services.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class GetServicesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetServicesHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoServices_ReturnsEmptyList()
    {
        List<ServiceResponse> result = await CreateSut().Handle(new GetServicesQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleActiveServices_ReturnsAllOfThem()
    {
        SeedService("A", isActive: true);
        SeedService("B", isActive: true);

        List<ServiceResponse> result = await CreateSut().Handle(new GetServicesQuery(), default);

        // Unlike DepositRule, many services can be active simultaneously — none should be
        // deactivated as a side effect of another being created.
        result.Should().HaveCount(2);
        result.Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task Handle_ActiveAndInactive_OrdersActiveFirstThenByName()
    {
        SeedService("Z-Inactive", isActive: false);
        SeedService("B-Active", isActive: true);
        SeedService("A-Active", isActive: true);

        List<ServiceResponse> result = await CreateSut().Handle(new GetServicesQuery(), default);

        result[0].Name.Should().Be("A-Active");
        result[1].Name.Should().Be("B-Active");
        result[2].Name.Should().Be("Z-Inactive");
    }

    // GetService (single) ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSingle_ExistingService_ReturnsService()
    {
        Service service = SeedService("My Service", isActive: true);

        GetServiceHandler handler = new(_db);
        ServiceResponse result = await handler.Handle(new GetServiceQuery(service.Id), default);

        result.Name.Should().Be("My Service");
    }

    [Fact]
    public async Task GetSingle_NonExistentId_ThrowsNotFoundException()
    {
        GetServiceHandler handler = new(_db);

        Func<Task> act = () => handler.Handle(new GetServiceQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private Service SeedService(string name, bool isActive)
    {
        Service service = new() { StudioId = _studioId, Name = name, DurationMinutes = 60, IsActive = isActive };
        _db.Services.Add(service);
        _db.SaveChanges();
        return service;
    }
}
