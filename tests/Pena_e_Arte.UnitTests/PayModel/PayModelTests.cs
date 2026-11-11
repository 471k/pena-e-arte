using FluentAssertions;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.Application.PayModel;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PayModel;

/// <summary>
/// A07a phase 4: the pay model as a fourth setup area, the studio's default terms (read, save,
/// confirm), one artist's own terms, the input rules, and the owner-only policies.
/// </summary>
public class PayModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public PayModelTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink", Slug = _studioId.ToString("N")[..8], OwnerEmail = "o@ink.test", Currency = "ALL" });
        _db.SaveChanges();
    }

    private Studio Studio => _db.Studios.Single(s => s.Id == _studioId);

    // ── setup readiness ─────────────────────────────────────────────────────

    [Fact]
    public void PayModel_IsNeverNotConfigured_OnlyUnreviewedOrReviewed()
    {
        SetupReadiness.PayModel(null).Should().Be(SetupAreaState.DefaultUnreviewed);
        SetupReadiness.PayModel(Now.UtcDateTime).Should().Be(SetupAreaState.Reviewed);
    }

    [Fact]
    public async Task ANewStudio_StartsOnCommissionWithNoRate_AndIsNotReviewed()
    {
        StudioPayModelResponse r = await new GetStudioPayModelHandler(_db, _tenant).Handle(new GetStudioPayModelQuery(), default);

        r.Should().Be(new StudioPayModelResponse("Commission", null, null, null, "ALL"));
        (await Readiness()).Should().Be(("DefaultUnreviewed", (DateTime?)null));
    }

    // ── saving the studio's terms ───────────────────────────────────────────

    [Fact]
    public async Task SavingTheForm_StoresTheTerms_AndMarksThemReviewed()
    {
        StudioPayModelResponse r = await Save(new UpdateStudioPayModelRequest("Commission", 62.5m, null));

        r.Should().Be(new StudioPayModelResponse("Commission", 62.5m, null, Now.UtcDateTime, "ALL"));
        Studio.DefaultPayArrangement.Should().Be(ArtistPayArrangement.Commission);
        Studio.DefaultCommissionRate.Should().Be(62.5m);
        (await Readiness()).Should().Be(("Reviewed", (DateTime?)Now.UtcDateTime));
    }

    [Fact]
    public async Task AFeeWithMoreDecimalsThanTheCurrency_IsRefused()
    {
        Studio.Currency = "JPY";
        await _db.SaveChangesAsync();

        Func<Task> act = () => Save(new UpdateStudioPayModelRequest("FlatFeePerSession", null, 10.5m));

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        Studio.PayModelReviewedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("Commission", null, null, false)]          // commission needs a rate
    [InlineData("FlatFeePerSession", null, null, false)]   // a flat fee needs a fee
    [InlineData("Commission", 60.0, null, true)]
    [InlineData("BoothRent", null, null, true)]
    [InlineData("Salaried", null, null, true)]
    [InlineData("FlatFeePerSession", null, 2500.0, true)]
    [InlineData("1", 60.0, null, false)]                   // a number is not an arrangement name
    [InlineData("commission", 60.0, null, false)]          // nor is another casing
    [InlineData("Commission", 100.5, null, false)]
    [InlineData("Commission", -1.0, null, false)]
    [InlineData("Commission", 60.125, null, false)]        // decimal(5,2)
    [InlineData("FlatFeePerSession", null, -1.0, false)]
    public void StudioTerms_Validation(string arrangement, double? rate, double? fee, bool valid)
    {
        ValidationResult result = new UpdateStudioPayModelValidator().Validate(
            new UpdateStudioPayModelCommand(new UpdateStudioPayModelRequest(arrangement, (decimal?)rate, (decimal?)fee)));

        result.IsValid.Should().Be(valid);
    }

    // ── confirming the studio's terms ───────────────────────────────────────

    [Fact]
    public async Task ConfirmingRegistrationsDefault_IsRefused_BecauseCommissionHasNoRate()
    {
        Func<Task> act = () => Confirm(Now);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).WithMessage("Set the commission rate first.");
        Studio.PayModelReviewedAt.Should().BeNull("a reviewed pay model must always give artists a share");
    }

    [Fact]
    public async Task ConfirmingTermsThatGiveAShare_MarksThemReviewed_AndKeepsTheFirstConfirmation()
    {
        Studio.DefaultPayArrangement = ArtistPayArrangement.BoothRent;
        await _db.SaveChangesAsync();

        await Confirm(Now);
        await Confirm(Now.AddDays(1));

        Studio.PayModelReviewedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task ConfirmingAFlatFeeWithNoFee_IsRefused()
    {
        Studio.DefaultPayArrangement = ArtistPayArrangement.FlatFeePerSession;
        await _db.SaveChangesAsync();

        Func<Task> act = () => Confirm(Now);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).WithMessage("Set the fee per session first.");
    }

    // ── one artist's own terms ──────────────────────────────────────────────

    [Fact]
    public async Task AnArtistsOwnTerms_AreSavedAndRead_AndNullMeansTheStudios()
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Costa", Email = "a@a.test" };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        ArtistPayModelResponse saved = await new UpdateArtistPayModelHandler(_db, _tenant).Handle(
            new UpdateArtistPayModelCommand(artist.Id, new UpdateArtistPayModelRequest("FlatFeePerSession", null, 3000m)), default);
        saved.Should().Be(new ArtistPayModelResponse(artist.Id, "FlatFeePerSession", null, 3000m));

        await new UpdateArtistPayModelHandler(_db, _tenant).Handle(
            new UpdateArtistPayModelCommand(artist.Id, new UpdateArtistPayModelRequest(null, 70m, null)), default);
        ArtistPayModelResponse read = await new GetArtistPayModelHandler(_db).Handle(new GetArtistPayModelQuery(artist.Id), default);

        read.Should().Be(new ArtistPayModelResponse(artist.Id, null, 70m, null));
    }

    [Fact]
    public async Task AnUnknownArtist_IsNotFound()
    {
        Func<Task> act = () => new GetArtistPayModelHandler(_db).Handle(new GetArtistPayModelQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("Salaried", null, null, true)]
    [InlineData("Commission", null, null, true)]   // the studio's rate applies
    [InlineData("Freelance", null, null, false)]
    [InlineData(null, 101.0, null, false)]
    public void ArtistTerms_Validation(string? arrangement, double? rate, double? fee, bool valid) =>
        new UpdateArtistPayModelValidator()
            .Validate(new UpdateArtistPayModelCommand(Guid.NewGuid(), new UpdateArtistPayModelRequest(arrangement, (decimal?)rate, (decimal?)fee)))
            .IsValid.Should().Be(valid);

    // ── policies ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/studios/me/pay-model")]
    [InlineData("PUT", "/api/v1/studios/me/pay-model")]
    [InlineData("POST", "/api/v1/studios/me/pay-model/confirm")]
    [InlineData("GET", "/api/v1/artists/{id:guid}/pay-model")]
    [InlineData("PUT", "/api/v1/artists/{id:guid}/pay-model")]
    public void EveryPayModelEndpoint_IsOwnerOnly(string method, string pattern)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<ISender>());
        // Some existing studio/artist endpoints take these straight from DI; mapping needs them registered.
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());
        builder.Services.AddSingleton(Substitute.For<ICurrentUser>());
        builder.Services.AddAuthorization();
        using WebApplication app = builder.Build();
        app.MapStudioEndpoints();
        app.MapArtistEndpoints();

        RouteEndpoint endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == pattern
                         && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().ContainSingle(a => a.Policy == "OwnerOnly");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Task<StudioPayModelResponse> Save(UpdateStudioPayModelRequest request) =>
        new UpdateStudioPayModelHandler(_db, _tenant, new FixedTimeProvider(Now)).Handle(new UpdateStudioPayModelCommand(request), default);

    private Task Confirm(DateTimeOffset at) =>
        new ConfirmStudioPayModelHandler(_db, _tenant, new FixedTimeProvider(at)).Handle(new ConfirmStudioPayModelCommand(), default);

    private async Task<(string State, DateTime? ReviewedAt)> Readiness()
    {
        SetupReadinessResponse r = await new GetSetupReadinessHandler(_db, _tenant).Handle(new GetSetupReadinessQuery(), default);
        SetupAreaResponse area = r.Areas.Single(a => a.Area == "PayModel");
        return (area.State, area.ReviewedAt);
    }
}
