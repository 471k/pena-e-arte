using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

/// <summary>
/// E05a (O02): setup readiness measures whether the owner has reviewed each area, not whether a row
/// exists. Registration seeds opening hours, so "hours rows exist" is true for every new studio and
/// proves nothing. The point of this suite is that a studio with only seeded rows is never "set up".
/// </summary>
public class SetupReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public SetupReadinessTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink", Slug = _studioId.ToString("N")[..8], OwnerEmail = "o@ink.test" });
        _db.SaveChanges();
    }

    // ── the pure derivation ─────────────────────────────────────────────────

    [Fact]
    public void Hours_NoRows_IsNotConfigured_EvenIfOnceConfirmed() =>
        SetupReadiness.Hours(false, Now.UtcDateTime).Should().Be(SetupAreaState.NotConfigured);

    [Fact]
    public void Hours_SeededRowsNobodyConfirmed_AreDefaultUnreviewed() =>
        SetupReadiness.Hours(true, null).Should().Be(SetupAreaState.DefaultUnreviewed);

    [Fact]
    public void Hours_Confirmed_AreReviewed() =>
        SetupReadiness.Hours(true, Now.UtcDateTime).Should().Be(SetupAreaState.Reviewed);

    [Theory]
    [InlineData(false, SetupAreaState.NotConfigured)]
    [InlineData(true, SetupAreaState.Reviewed)]
    public void OwnerCreatedAreas_HaveNoDefaultState(bool anyExist, SetupAreaState expected) =>
        SetupReadiness.OwnerCreated(anyExist).Should().Be(expected);

    // ── the handler ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ANewStudio_WithOnlyRegistrationsHours_IsNotSetUp()
    {
        SeedRegistrationHours();

        Dictionary<string, string> states = await States();

        states.Should().Equal(new Dictionary<string, string>
        {
            ["Artists"] = "NotConfigured",
            ["DepositRules"] = "NotConfigured",
            ["Hours"] = "DefaultUnreviewed",
        });
        states.Values.Should().NotContain("Reviewed", "nothing here was done by the owner");
    }

    [Fact]
    public async Task AStudioWithNoHoursAtAll_IsNotConfigured()
    {
        (await States())["Hours"].Should().Be("NotConfigured");
    }

    [Fact]
    public async Task ArtistsAndDepositRules_AreReviewedOnceTheOwnerCreatesThem()
    {
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.test" });
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "Standard", AmountFixed = 50m, IsActive = true });
        await _db.SaveChangesAsync();

        Dictionary<string, string> states = await States();

        states["Artists"].Should().Be("Reviewed");
        states["DepositRules"].Should().Be("Reviewed");
    }

    [Fact]
    public async Task SavingTheHoursForm_MarksThemReviewed_EvenUnchanged()
    {
        SeedRegistrationHours();

        await new UpsertStudioHoursHandler(_db, _tenant).Handle(
            new UpsertStudioHoursCommand([new StudioHoursEntryDto(DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), true)]),
            default);

        SetupReadinessResponse response = await Read();
        SetupAreaResponse hours = response.Areas.Single(a => a.Area == "Hours");
        hours.State.Should().Be("Reviewed");
        hours.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ASeededRowEditedLater_StaysReviewedAfterFurtherEdits()
    {
        SeedRegistrationHours();
        UpsertStudioHoursHandler upsert = new(_db, _tenant);

        await upsert.Handle(new UpsertStudioHoursCommand(
            [new StudioHoursEntryDto(DayOfWeek.Monday, new TimeSpan(10, 0, 0), new TimeSpan(19, 0, 0), true)]), default);
        await upsert.Handle(new UpsertStudioHoursCommand(
            [new StudioHoursEntryDto(DayOfWeek.Saturday, new TimeSpan(10, 0, 0), new TimeSpan(14, 0, 0), true)]), default);

        (await States())["Hours"].Should().Be("Reviewed");
    }

    [Fact]
    public async Task ConfirmingTheDefaults_MarksThemReviewed_AndKeepsTheFirstConfirmation()
    {
        SeedRegistrationHours();
        ConfirmStudioHoursHandler confirm = new(_db, _tenant, new FixedTimeProvider(Now));

        await confirm.Handle(new ConfirmStudioHoursCommand(), default);
        await new ConfirmStudioHoursHandler(_db, _tenant, new FixedTimeProvider(Now.AddDays(1)))
            .Handle(new ConfirmStudioHoursCommand(), default);

        SetupAreaResponse hours = (await Read()).Areas.Single(a => a.Area == "Hours");
        hours.State.Should().Be("Reviewed");
        hours.ReviewedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task ConfirmingHours_ThatDontExist_IsRefused_SoAMissingAreaCantBeSilenced()
    {
        Func<Task> act = () => new ConfirmStudioHoursHandler(_db, _tenant, new FixedTimeProvider(Now))
            .Handle(new ConfirmStudioHoursCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        (await States())["Hours"].Should().Be("NotConfigured");
    }

    [Fact]
    public async Task AreasComeBackInAFixedOrder()
    {
        (await Read()).Areas.Select(a => a.Area).Should().Equal("Artists", "DepositRules", "Hours");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>What RegisterStudioHandler writes: Mon–Fri 09:00–18:00, nothing confirmed.</summary>
    private void SeedRegistrationHours()
    {
        foreach (DayOfWeek day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            _db.StudioHours.Add(new StudioHours { StudioId = _studioId, DayOfWeek = day, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(18, 0, 0) });
        }
        _db.SaveChanges();
    }

    private Task<SetupReadinessResponse> Read() =>
        new GetSetupReadinessHandler(_db, _tenant).Handle(new GetSetupReadinessQuery(), default);

    private async Task<Dictionary<string, string>> States() =>
        (await Read()).Areas.ToDictionary(a => a.Area, a => a.State);
}
