using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetMyStudioHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private GetMyStudioHandler CreateSut() => new(_db, _tenant);

    private async Task<Studio> SeedStudioAsync()
    {
        Studio studio = new()
        {
            Name = "Test",
            Slug = "test",
            City = "Tirana",
            CountryCode = "AL",
            Currency = "ALL",
            IsActive = true,
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(studio.Id);
        return studio;
    }

    [Fact]
    public async Task Handle_NoMoneyMoved_CurrencyNotLocked()
    {
        await SeedStudioAsync();

        StudioResponse result = await CreateSut().Handle(new GetMyStudioQuery(), default);

        result.CurrencyLocked.Should().BeFalse();
        result.Currency.Should().Be("ALL");
        result.CountryCode.Should().Be("AL");
    }

    [Fact]
    public async Task Handle_AfterFirstPayment_CurrencyLocked()
    {
        Studio studio = await SeedStudioAsync();
        _db.Payments.Add(new Payment
        {
            StudioId = studio.Id,
            AppointmentId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Currency = "ALL",
            Status = PaymentStatus.Paid,
        });
        await _db.SaveChangesAsync();

        StudioResponse result = await CreateSut().Handle(new GetMyStudioQuery(), default);

        result.CurrencyLocked.Should().BeTrue();
    }
}
