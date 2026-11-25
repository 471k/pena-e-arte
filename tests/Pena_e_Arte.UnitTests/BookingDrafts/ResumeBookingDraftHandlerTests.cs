using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.BookingDrafts;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.BookingDrafts;

/// <summary>C05: both proofs, one not-found, and the ClaimedAt filter that keeps an unclaimed draft invisible.</summary>
public class ResumeBookingDraftHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly BookingDraftResumeTokens _tokens = new(new EphemeralDataProtectionProvider());
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly FakeCurrentUser _owner = FakeCurrentUser.Client();

    private BookingDraft Unclaimed(Guid? userId = null)
    {
        DateTime createdAt = _time.GetUtcNow().UtcDateTime;
        BookingDraft draft = new()
        {
            StudioId = _studioId,
            UserId = userId ?? _owner.UserId,
            PayloadJson = """{"intake":{"tattooDescription":"A swallow"}}""",
            CreatedAt = createdAt,
            ExpiresAt = createdAt + BookingDraft.Lifetime,
        };
        _db.BookingDrafts.Add(draft);
        _db.SaveChanges();
        return draft;
    }

    private Task<ResumeBookingDraftResponse> Resume(string token, FakeCurrentUser? as_ = null) =>
        new ResumeBookingDraftHandler(_db, as_ ?? _owner, _tokens, _time).Handle(new ResumeBookingDraftCommand(token), default);

    private Task<BookingDraftResponse?> Mine(FakeCurrentUser? as_ = null) =>
        new GetMyBookingDraftHandler(_db, as_ ?? _owner, _time).Handle(new GetMyBookingDraftQuery(), default);

    [Fact]
    public async Task Get_DoesNotReturnAnUnclaimedDraft()
    {
        // The one filter that makes C05 safe. Remove `ClaimedAt != null` from GetMyBookingDraftHandler and this fails.
        Unclaimed();

        (await Mine()).Should().BeNull();
    }

    [Fact]
    public async Task Resume_WithTheOwnersSignIn_ClaimsTheDraft_AndItBecomesVisible()
    {
        BookingDraft draft = Unclaimed();

        ResumeBookingDraftResponse result = await Resume(_tokens.Mint(draft));

        result.Outcome.Should().Be(ResumeBookingDraftHandler.Resumed);
        result.Draft!.Payload.Should().Contain("A swallow");
        _db.BookingDrafts.Single().ClaimedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        (await Mine()).Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_Twice_SaysAlreadyClaimed()
    {
        BookingDraft draft = Unclaimed();
        string token = _tokens.Mint(draft);
        await Resume(token);

        ResumeBookingDraftResponse again = await Resume(token);

        again.Outcome.Should().Be(ResumeBookingDraftHandler.AlreadyClaimed);
        again.Draft.Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_AfterSevenDays_SaysExpired_EvenWhenTheRowIsGone()
    {
        BookingDraft draft = Unclaimed();
        string token = _tokens.Mint(draft);
        _db.BookingDrafts.Remove(draft);
        await _db.SaveChangesAsync();
        _time.Advance(BookingDraft.Lifetime + TimeSpan.FromSeconds(1));

        ResumeBookingDraftResponse result = await Resume(token);

        result.Outcome.Should().Be(ResumeBookingDraftHandler.Expired);
        result.Draft.Should().BeNull();
    }

    [Fact]
    public async Task Resume_TheFourFailures_AreOneIdenticalNotFound()
    {
        // 1. a forged token
        string? forged = await NotFoundMessage(() => Resume("forged-token"));
        // 2. a draft that is gone
        BookingDraft gone = Unclaimed(Guid.NewGuid());
        string goneToken = _tokens.Mint(gone);
        _db.BookingDrafts.Remove(gone);
        await _db.SaveChangesAsync();
        string? goneMsg = await NotFoundMessage(() => Resume(goneToken));
        // 3. a row past its time while the token's own window is open (the row is the authority for this case)
        BookingDraft stale = Unclaimed(Guid.NewGuid());
        stale.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        await _db.SaveChangesAsync();
        FakeCurrentUser staleOwner = new(stale.UserId, "client");
        string? staleMsg = await NotFoundMessage(() => Resume(_tokens.Mint(stale), staleOwner));
        // 4. someone else's draft
        BookingDraft someoneElses = Unclaimed(Guid.NewGuid());
        string? wrongUser = await NotFoundMessage(() => Resume(_tokens.Mint(someoneElses)));

        new[] { forged, goneMsg, staleMsg, wrongUser }.Should().AllBe(forged).And.NotContainNulls();
        forged.Should().NotContain(someoneElses.Id.ToString());
        _db.BookingDrafts.Single(d => d.Id == someoneElses.Id).ClaimedAt.Should().BeNull("a refused resume claims nothing");
    }

    [Fact]
    public async Task Save_ClaimsTheDraftTheOwnerSavesThemselves()
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(_studioId);

        await new SaveMyBookingDraftHandler(_db, _owner, tenant, _time)
            .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest("""{"intake":{}}""", null)), default);

        _db.BookingDrafts.Single().ClaimedAt.Should().NotBeNull();
        (await Mine()).Should().NotBeNull();
    }

    [Fact]
    public async Task Save_OverAnUnclaimedDraft_ReplacesItsContentAndClaimsIt()
    {
        Unclaimed();
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(_studioId);

        await new SaveMyBookingDraftHandler(_db, _owner, tenant, _time)
            .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest("""{"intake":{"tattooDescription":"mine"}}""", null)), default);

        BookingDraft only = _db.BookingDrafts.Single();
        only.PayloadJson.Should().NotContain("A swallow", "the stranger's text is replaced, never claimed");
        only.ClaimedAt.Should().NotBeNull();
    }

    private static async Task<string?> NotFoundMessage(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (NotFoundException ex)
        {
            return ex.Message;
        }
    }
}
