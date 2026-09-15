using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class CreateServiceHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateServiceHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateServiceHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsServiceResponse()
    {
        CreateServiceRequest req = new("New Tattoo Session", "First session", 90, 150m, 50m, true);

        ServiceResponse result = await CreateSut().Handle(new CreateServiceCommand(req), default);

        result.Name.Should().Be("New Tattoo Session");
        result.Description.Should().Be("First session");
        result.DurationMinutes.Should().Be(90);
        result.Price.Should().Be(150m);
        result.DepositAmount.Should().Be(50m);
        result.IsActive.Should().BeTrue();
        result.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsServiceToDb()
    {
        CreateServiceRequest req = new("Touch-Up", null, 30, null, null, true);

        await CreateSut().Handle(new CreateServiceCommand(req), default);

        _db.Services.Should().ContainSingle(s => s.Name == "Touch-Up" && s.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_MultipleActiveServices_DoesNotDeactivateEachOther()
    {
        await CreateSut().Handle(new CreateServiceCommand(new("First", null, 60, null, null, true)), default);
        await CreateSut().Handle(new CreateServiceCommand(new("Second", null, 60, null, null, true)), default);

        _db.Services.Should().HaveCount(2);
        _db.Services.Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task Handle_NullPriceAndDeposit_PersistsAsNull()
    {
        CreateServiceRequest req = new("Consultation", null, 15, null, null, true);

        ServiceResponse result = await CreateSut().Handle(new CreateServiceCommand(req), default);

        result.Price.Should().BeNull();
        result.DepositAmount.Should().BeNull();
    }
}
