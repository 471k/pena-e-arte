using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

/// <summary>The studio's slot-list step setting (C03 follow-up, Phi 20 Nov).</summary>
public class BookingSettingsTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public BookingSettingsTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s", OwnerEmail = "o@x.test" });
        _db.SaveChanges();
    }

    [Fact]
    public async Task A_new_studio_has_no_step_meaning_every_session_length_and_is_told_the_choices()
    {
        BookingSettingsResponse result = await new GetMyBookingSettingsHandler(_db, _tenant).Handle(new GetMyBookingSettingsQuery(), default);

        result.SlotStepMinutes.Should().BeNull();
        result.AllowedStepMinutes.Should().Equal(15, 30, 60);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(null)]
    public async Task The_owner_sets_or_clears_the_step(int? step)
    {
        BookingSettingsResponse result = await new UpdateMyBookingSettingsHandler(_db, _tenant)
            .Handle(new UpdateMyBookingSettingsCommand(new UpdateBookingSettingsRequest(step)), default);

        result.SlotStepMinutes.Should().Be(step);
        _db.ChangeTracker.Clear();
        (await _db.Studios.FindAsync(_studioId))!.BookingSlotStepMinutes.Should().Be(step);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(45, false)]
    [InlineData(120, false)]
    public void Only_the_offered_steps_are_accepted(int? step, bool valid)
    {
        new UpdateMyBookingSettingsValidator()
            .Validate(new UpdateMyBookingSettingsCommand(new UpdateBookingSettingsRequest(step)))
            .IsValid.Should().Be(valid);
    }
}
