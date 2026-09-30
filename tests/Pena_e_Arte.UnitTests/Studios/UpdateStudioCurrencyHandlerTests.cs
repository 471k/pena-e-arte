using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateStudioCurrencyHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private UpdateStudioCurrencyHandler CreateSut() => new(_db, _tenant);

    private async Task<Studio> SeedStudioAsync(string currency = "ALL")
    {
        Studio studio = new()
        {
            Name = "Test Studio",
            Slug = "test-studio",
            City = "Tirana",
            CountryCode = "AL",
            Currency = currency,
            IsActive = true,
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(studio.Id);
        return studio;
    }

    [Fact]
    public async Task Handle_NoMoneyMoved_ChangesCurrency()
    {
        Studio studio = await SeedStudioAsync("ALL");

        StudioResponse result = await CreateSut()
            .Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        result.Currency.Should().Be("EUR");
        _db.Studios.Single().Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_ChangeToSameCurrency_IsNoOp()
    {
        Studio studio = await SeedStudioAsync("EUR");

        StudioResponse result = await CreateSut()
            .Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        result.Currency.Should().Be("EUR");
        result.CurrencyLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AfterCashPayment_ThrowsBusinessRuleViolationException()
    {
        Studio studio = await SeedStudioAsync("ALL");
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

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*can't be changed*");
        _db.Studios.Single().Currency.Should().Be("ALL");
    }

    [Fact]
    public async Task Handle_AfterGiftCardSold_ThrowsBusinessRuleViolationException()
    {
        Studio studio = await SeedStudioAsync("ALL");
        _db.GiftCards.Add(new GiftCard
        {
            StudioId = studio.Id,
            Code = "ABCDEFGHJKMN",
            InitialBalance = 50m,
            RemainingBalance = 50m,
            Currency = "ALL",
            PurchaserEmail = "buyer@example.com",
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AfterPackagePurchase_ThrowsBusinessRuleViolationException()
    {
        Studio studio = await SeedStudioAsync("ALL");
        _db.PackagePurchases.Add(new PackagePurchase
        {
            StudioId = studio.Id,
            PackageId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "ALL",
            Provider = "pok",
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AfterBoothRentCharge_ThrowsBusinessRuleViolationException()
    {
        Studio studio = await SeedStudioAsync("ALL");
        _db.BoothRentCharges.Add(new BoothRentCharge
        {
            StudioId = studio.Id,
            BoothRentScheduleId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "ALL",
            ChargedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_SoftDeletedPaymentStillLocksCurrency()
    {
        // IgnoreQueryFilters() is deliberate in StudioCurrencyLock — money that moved and was
        // later soft-deleted still moved.
        Studio studio = await SeedStudioAsync("ALL");
        _db.Payments.Add(new Payment
        {
            StudioId = studio.Id,
            AppointmentId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Currency = "ALL",
            Status = PaymentStatus.Paid,
            DeletedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PaymentInAnotherStudio_DoesNotLockThisStudio()
    {
        Studio studio = await SeedStudioAsync("ALL");
        _db.Payments.Add(new Payment
        {
            StudioId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Currency = "ALL",
            Status = PaymentStatus.Paid,
        });
        await _db.SaveChangesAsync();

        StudioResponse result = await CreateSut()
            .Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_AuditableCommand_CarriesStudioIdAsTarget()
    {
        Guid studioId = Guid.NewGuid();
        IAuditableCommand command = new UpdateStudioCurrencyCommand(studioId, "EUR");

        command.AuditAction.Should().Be("Studio.CurrencyChanged");
        command.AuditTargetType.Should().Be("Studio");
        command.AuditTargetId.Should().Be(studioId);
    }

    [Fact]
    public async Task Handle_StudioBelongsToAnotherTenant_ThrowsNotFoundException()
    {
        Studio studio = await SeedStudioAsync("ALL");
        _tenant.StudioId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new UpdateStudioCurrencyCommand(studio.Id, "EUR"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
