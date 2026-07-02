using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class DesignHandlerIntegrationTests(DatabaseFixture fixture)
{
    private readonly IRealtimeNotifier _realtime     = Substitute.For<IRealtimeNotifier>();
    private readonly ISender           _sender       = Substitute.For<ISender>();
    private readonly IJobScheduler     _jobScheduler = Substitute.For<IJobScheduler>();

    // ── CreateDesign ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDesign_WithValidForeignKeys_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CreateDesignHandler handler = new(db, TenantFor(tenantId), OwnerUser());

        DesignResponse result = await handler.Handle(
            new CreateDesignCommand(new CreateDesignRequest(clientId, artistId, "Rose tattoo", "Small wrist rose")),
            default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.Designs.AnyAsync(d => d.Id == result.Id);
        exists.Should().BeTrue();
    }

    // ── UploadDesignRevision ──────────────────────────────────────────────────────

    [Fact]
    public async Task UploadDesignRevision_FirstRevision_SetsVersionOne()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);

        DesignRevisionResponse result = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        result.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task UploadDesignRevision_SecondRevision_IncrementsVersion()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);

        await RunUploadHandler(tenantId, new(designId, "https://r2.example.com/v1.png", null));
        DesignRevisionResponse result = await RunUploadHandler(tenantId, new(designId, "https://r2.example.com/v2.png", null));

        result.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task UploadDesignRevision_DesignNotFound_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();

        Func<Task> act = () => RunUploadHandler(
            tenantId, new(Guid.NewGuid(), "https://r2.example.com/v1.png", null));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UploadDesignRevision_DesignFromDifferentTenant_ThrowsNotFoundException()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantA);

        // tenantB handler won't see tenantA's design due to query filter
        Func<Task> act = () => RunUploadHandler(
            tenantB, new(designId, "https://r2.example.com/v1.png", null));

        await act.Should().ThrowAsync<NotFoundException>(
            because: "the query filter prevents tenantB from finding tenantA's design");
    }

    // ── ReviewDesign ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewDesign_ApproveRevision_CreatesApprovalRecord()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        await RunReviewHandler(tenantId, new(revision.Id, Approved: true, Notes: null));

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.DesignApprovals
            .AnyAsync(a => a.DesignRevisionId == revision.Id && a.Status == DesignApprovalStatus.Approved);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewDesign_AlreadyApproved_ThrowsDesignAlreadyApprovedException()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        await RunReviewHandler(tenantId, new(revision.Id, Approved: true, Notes: null));

        Func<Task> act = () => RunReviewHandler(tenantId, new(revision.Id, Approved: true, Notes: null));

        await act.Should().ThrowAsync<DesignAlreadyApprovedException>();
    }

    [Fact]
    public async Task ReviewDesign_RequestChanges_UpdatesToChangesRequestedStatus()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        await RunReviewHandler(tenantId, new(revision.Id, Approved: false, Notes: "Fix the shading"));

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DesignApproval? approval = await verify.DesignApprovals
            .FirstOrDefaultAsync(a => a.DesignRevisionId == revision.Id);

        approval!.Status.Should().Be(DesignApprovalStatus.ChangesRequested);
        approval.ClientNotes.Should().Be("Fix the shading");
    }

    // ── DesignShareToken ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSharedDesign_ValidToken_ReturnsSharedDesignResponse()
    {
        Guid tenantId  = Guid.NewGuid();
        Guid designId  = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        DesignShareTokenResponse tokenData = await RunCreateShareTokenHandler(tenantId, revision.Id);

        IR2Service r2 = Substitute.For<IR2Service>();
        r2.GeneratePresignedReadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns("https://signed.example.com/url");

        await using AppDbContext publicDb = fixture.CreateDbContext(Guid.Empty);
        GetSharedDesignHandler handler = new(publicDb, r2);
        SharedDesignResponse? result = await handler.Handle(new GetSharedDesignQuery(tokenData.Token), default);

        result.Should().NotBeNull();
        result!.ImageUrl.Should().Be("https://signed.example.com/url");
    }

    [Fact]
    public async Task GetSharedDesign_ExpiredToken_ReturnsNull()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        DesignShareTokenResponse tokenData = await RunCreateShareTokenHandler(tenantId, revision.Id);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        DesignShareToken shareToken = ctx.DesignShareTokens.First(t => t.Token == tokenData.Token);
        shareToken.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await ctx.SaveChangesAsync();

        IR2Service r2 = Substitute.For<IR2Service>();
        await using AppDbContext publicDb = fixture.CreateDbContext(Guid.Empty);
        GetSharedDesignHandler handler = new(publicDb, r2);
        SharedDesignResponse? result = await handler.Handle(new GetSharedDesignQuery(tokenData.Token), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSharedDesign_RevokedToken_ReturnsNull()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);
        DesignRevisionResponse revision = await RunUploadHandler(
            tenantId, new(designId, "https://r2.example.com/v1.png", null));

        DesignShareTokenResponse tokenData = await RunCreateShareTokenHandler(tenantId, revision.Id);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        DesignShareToken shareToken = ctx.DesignShareTokens.First(t => t.Token == tokenData.Token);
        shareToken.IsRevoked = true;
        await ctx.SaveChangesAsync();

        IR2Service r2 = Substitute.For<IR2Service>();
        await using AppDbContext publicDb = fixture.CreateDbContext(Guid.Empty);
        GetSharedDesignHandler handler = new(publicDb, r2);
        SharedDesignResponse? result = await handler.Handle(new GetSharedDesignQuery(tokenData.Token), default);

        result.Should().BeNull();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<(Guid ArtistId, Guid ClientId)> SeedArtistAndClient(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return (artist.Id, client.Id);
    }

    private async Task<Guid> SeedDesign(Guid tenantId)
    {
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Studio studio = new()
        {
            Id         = tenantId,
            Name       = "Test Studio",
            Slug       = tenantId.ToString("N")[..16],
            OwnerEmail = "owner@test.studio",
        };
        ctx.Studios.Add(studio);
        Design design = new() { StudioId = tenantId, ClientId = clientId, ArtistId = artistId, Title = "Rose" };
        ctx.Designs.Add(design);
        await ctx.SaveChangesAsync();
        return design.Id;
    }

    private async Task<DesignRevisionResponse> RunUploadHandler(Guid tenantId, UploadDesignRevisionRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UploadDesignRevisionHandler handler = new(db, TenantFor(tenantId), _realtime, _jobScheduler, OwnerUser());
        return await handler.Handle(new UploadDesignRevisionCommand(req), default);
    }

    private async Task RunReviewHandler(Guid tenantId, ReviewDesignRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        ReviewDesignHandler handler = new(db, TenantFor(tenantId), OwnerUser(), _realtime, _sender);
        await handler.Handle(new ReviewDesignCommand(req), default);
    }

    private async Task<DesignShareTokenResponse> RunCreateShareTokenHandler(Guid tenantId, Guid revisionId)
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CreateDesignShareTokenHandler handler = new(db, TenantFor(tenantId), currentUser);
        return await handler.Handle(new CreateDesignShareTokenCommand(revisionId), default);
    }

    private static ICurrentTenant TenantFor(Guid tenantId)
    {
        CurrentTenantService t = new();
        t.SetTenant(tenantId);
        return t;
    }

    private static ICurrentUser OwnerUser()
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Role.Returns("owner");
        return currentUser;
    }
}
