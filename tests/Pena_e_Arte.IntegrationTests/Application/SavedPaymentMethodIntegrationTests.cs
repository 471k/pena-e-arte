using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Application.SavedPaymentMethods.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class SavedPaymentMethodIntegrationTests(DatabaseFixture fixture)
{
    // ── Tenant isolation — the exact class of bug an in-memory unit test can't catch, since it
    //    relies on AppDbContext's real, MySQL-backed global HasQueryFilter. A card saved at one
    //    studio must never be visible/usable at another (ADR-0001: per-studio POK merchant). ──

    [Fact]
    public async Task GetSavedPaymentMethods_ClientWithCardsAtTwoStudios_ReturnsOnlyCurrentStudiosCard()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        Guid clientAId = await SeedClient(studioA, userId);
        Guid clientBId = await SeedClient(studioB, userId);

        await SeedSavedMethod(studioA, clientAId, "card-a");
        await SeedSavedMethod(studioB, clientBId, "card-b");

        await using AppDbContext db = fixture.CreateDbContext(studioA);
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        GetSavedPaymentMethodsHandler handler = new(db, user);

        List<SavedPaymentMethodResponse> result = await handler.Handle(new GetSavedPaymentMethodsQuery(), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteSavedPaymentMethod_CardFromAnotherStudio_ThrowsNotFoundException()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        await SeedClient(studioA, userId);
        Guid clientBId = await SeedClient(studioB, userId);
        Guid methodBId = await SeedSavedMethod(studioB, clientBId, "card-b");

        await using AppDbContext db = fixture.CreateDbContext(studioA);
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        DeleteSavedPaymentMethodHandler handler = new(db, user);

        Func<Task> act = () => handler.Handle(new DeleteSavedPaymentMethodCommand(methodBId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddSavedPaymentMethod_ValidRequest_PersistsScopedToCurrentStudioOnly()
    {
        Guid userId = Guid.NewGuid();
        Guid studioId = Guid.NewGuid();
        await SeedClient(studioId, userId);

        IPokCardTokenService cardTokens = Substitute.For<IPokCardTokenService>();
        cardTokens.TokenizeCardAsync(studioId, "jwe-value", Arg.Any<string?>(), Arg.Any<PokCardBillingInfo>(), Arg.Any<CancellationToken>())
            .Returns(new PokTokenizedCard("card-real-db", "Visa", "**** 4242", "12", "2030"));

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        AddSavedPaymentMethodHandler handler = new(db, tenant, user, cardTokens);

        AddSavedPaymentMethodRequest req = new(
            "jwe-value", "123", "Jamie", "Client", "jamie@test.com", "AL", "Tirana", "Tirana",
            "Rr. Myslym Shyri 10", "1001", "+355691234567");
        SavedPaymentMethodResponse result = await handler.Handle(new AddSavedPaymentMethodCommand(req), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        SavedPaymentMethod? saved = await verify.SavedPaymentMethods.FindAsync(result.Id);
        saved!.StudioId.Should().Be(studioId);
        saved.ProviderCardTokenId.Should().Be("card-real-db");
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedClient(Guid studioId, Guid userId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId = studioId,
            UserId = userId,
            FirstName = "A",
            LastName = "B",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedSavedMethod(Guid studioId, Guid clientId, string providerCardTokenId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        SavedPaymentMethod method = new() { StudioId = studioId, ClientId = clientId, ProviderCardTokenId = providerCardTokenId };
        ctx.SavedPaymentMethods.Add(method);
        await ctx.SaveChangesAsync();
        return method.Id;
    }
}
