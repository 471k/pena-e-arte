using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.GiftCards.Commands;
using Pena_e_Arte.Application.GiftCards.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class GiftCardHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task PurchaseGiftCard_ThenReconciliationConfirms_ActivatesCard()
    {
        Guid tenantId = Guid.NewGuid();
        string slug = await SeedPublishedStudio(tenantId);

        IPaymentProvider provider = Substitute.For<IPaymentProvider>();
        provider.CreatePaymentHoldAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(("pi_gift_test", "secret_gift_test"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        PurchaseGiftCardHandler handler = new(db, provider);

        PurchaseGiftCardResponse purchaseResult = await handler.Handle(
            new PurchaseGiftCardCommand(new PurchaseGiftCardRequest(slug, 100m, "buyer@example.com", null)), default);

        await using AppDbContext verify1 = fixture.CreateDbContext(tenantId);
        GiftCard card = await verify1.GiftCards.IgnoreQueryFilters().FirstAsync(g => g.Id == purchaseResult.GiftCardId);
        card.Status.Should().Be(GiftCardStatus.Pending);

        provider.GetStatusAsync("pi_gift_test", Arg.Any<CancellationToken>()).Returns("succeeded");

        await using AppDbContext reconcileDb = fixture.CreateDbContext(Guid.Empty);
        GiftCardReconciliationJob job = new(reconcileDb, provider);
        await job.RunAsync();

        await using AppDbContext verify2 = fixture.CreateDbContext(tenantId);
        GiftCard confirmed = await verify2.GiftCards.IgnoreQueryFilters().FirstAsync(g => g.Id == purchaseResult.GiftCardId);
        confirmed.Status.Should().Be(GiftCardStatus.Active);
    }

    [Fact]
    public async Task GetGiftCardBalance_NeverIncludesPurchaserOrRecipientEmail()
    {
        Guid tenantId = Guid.NewGuid();
        string code = await SeedActiveGiftCard(tenantId, 42m);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetGiftCardBalanceHandler handler = new(db);

        GiftCardBalanceResponse result = await handler.Handle(new GetGiftCardBalanceQuery(code), default);

        result.RemainingBalance.Should().Be(42m);
        result.Status.Should().Be(GiftCardStatus.Active.ToString());
        // GiftCardBalanceResponse's own shape has no email fields at all — the compiler
        // enforces the "never leaks" guarantee; this assertion documents that intent.
        typeof(GiftCardBalanceResponse).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[] { "PurchaserEmail", "RecipientEmail" });
    }

    private async Task<string> SeedPublishedStudio(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        string slug = $"studio-{Guid.NewGuid():N}";
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Test Studio", Slug = slug, IsActive = true, IsPublished = true });
        await ctx.SaveChangesAsync();
        return slug;
    }

    private async Task<string> SeedActiveGiftCard(Guid tenantId, decimal balance)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        string code = $"BAL{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        ctx.GiftCards.Add(new GiftCard
        {
            StudioId = tenantId,
            Code = code,
            InitialBalance = balance,
            RemainingBalance = balance,
            PurchaserEmail = "buyer@example.com",
            Status = GiftCardStatus.Active,
            Provider = "pok",
        });
        await ctx.SaveChangesAsync();
        return code;
    }
}
