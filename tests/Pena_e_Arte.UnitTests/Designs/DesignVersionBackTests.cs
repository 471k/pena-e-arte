using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

/// <summary>
/// 2026-11-06 (Phi: a client may go back to an earlier version they preferred). Review is latest-only; a
/// client asks for an earlier version back, recorded as "changes requested" on the latest naming the version;
/// the artist brings it back as the newest version through an ordinary upload.
/// </summary>
public class DesignVersionBackTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();
    private readonly Guid _artistUserId = Guid.NewGuid();
    private readonly Guid _designId;
    private readonly Guid _artistId;
    private readonly DesignRevision _v1;
    private readonly DesignRevision _v2;
    private readonly DesignRevision _v3;

    public DesignVersionBackTests()
    {
        _tenant.StudioId.Returns(_studioId);
        Client client = new() { StudioId = _studioId, UserId = _clientUserId, FirstName = "C", LastName = "D", Email = "c@c.test" };
        Artist artist = new() { StudioId = _studioId, UserId = _artistUserId, FirstName = "A", LastName = "B", Email = "a@a.test" };
        _db.Clients.Add(client);
        _db.Artists.Add(artist);
        Design design = new() { StudioId = _studioId, ClientId = client.Id, ArtistId = artist.Id, Title = "Koi" };
        _db.Designs.Add(design);
        _v1 = Revision(design.Id, 1, DesignApprovalStatus.ChangesRequested);
        _v2 = Revision(design.Id, 2, DesignApprovalStatus.ChangesRequested);
        _v3 = Revision(design.Id, 3, null);
        _db.SaveChanges();
        _designId = design.Id;
        _artistId = artist.Id;
        _sender.Send(Arg.Any<ReviewDesignCommand>(), Arg.Any<CancellationToken>())
            .Returns(new DesignRevisionResponse(_v3.Id, _designId, 3, "u", null, DateTime.UtcNow, "ChangesRequested", "n"));
        _sender.Send(Arg.Any<UploadDesignRevisionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new DesignRevisionResponse(Guid.NewGuid(), _designId, 4, "u", null, DateTime.UtcNow, null, null));
    }

    private DesignRevision Revision(Guid designId, int version, DesignApprovalStatus? status)
    {
        DesignRevision r = new()
        {
            StudioId = _studioId,
            DesignId = designId,
            VersionNumber = version,
            FileUrl = $"https://r2.example.com/v{version}.png",
            UploadedAt = DateTime.UtcNow.AddDays(version - 10),
        };
        if (status is { } s)
            r.Approval = new DesignApproval { StudioId = _studioId, DesignRevisionId = r.Id, Status = s, ReviewedAt = DateTime.UtcNow };
        _db.DesignRevisions.Add(r);
        return r;
    }

    private FakeCurrentUser Client() => new(_clientUserId, "client");

    private Task<DesignRevisionResponse> Review(Guid revisionId, bool approved, FakeCurrentUser? user = null) =>
        new ReviewDesignHandler(_db, _tenant, user ?? Client(), Substitute.For<IRealtimeNotifier>(), _sender)
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, approved, null)), default);

    private Task<DesignRevisionResponse> AskBack(Guid revisionId, string? notes = null, FakeCurrentUser? user = null) =>
        new RequestDesignVersionAgainHandler(_db, user ?? Client(), _sender)
            .Handle(new RequestDesignVersionAgainCommand(revisionId, notes), default);

    private Task<DesignRevisionResponse> Restore(Guid revisionId) =>
        new RestoreDesignRevisionHandler(_db, _sender).Handle(new RestoreDesignRevisionCommand(_designId, revisionId), default);

    // ── Latest-only review ──────────────────────────────────────────────────

    [Fact]
    public async Task ReviewingAnOlderVersion_IsRefused_NamingTheNewerOne()
    {
        Func<Task> act = () => Review(_v2.Id, approved: true);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .WithMessage("Version 3 is newer. Review version 3, or ask for version 2 back.");
        _db.DesignApprovals.Single(a => a.DesignRevisionId == _v2.Id).Status.Should().Be(DesignApprovalStatus.ChangesRequested);
    }

    [Fact]
    public async Task StaffAreHeldToLatestOnlyToo()
    {
        Func<Task> act = () => Review(_v1.Id, approved: true, FakeCurrentUser.Owner());

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task ReviewingTheLatestVersion_StillWorks()
    {
        await Review(_v3.Id, approved: true);

        _db.DesignApprovals.Single(a => a.DesignRevisionId == _v3.Id).Status.Should().Be(DesignApprovalStatus.Approved);
    }

    [Fact]
    public async Task ADeletedNewerVersion_DoesNotBlockReviewingTheNowLatestOne()
    {
        _v3.DeletedAt = DateTime.UtcNow;
        _db.SaveChanges();

        await Review(_v2.Id, approved: true);

        _db.DesignApprovals.Single(a => a.DesignRevisionId == _v2.Id).Status.Should().Be(DesignApprovalStatus.Approved);
    }

    // ── Asking for a version back ───────────────────────────────────────────

    [Fact]
    public async Task AskingForAVersionBack_RequestsChangesOnTheLatest_NamingTheVersion()
    {
        await AskBack(_v1.Id, "  The first linework was better.  ");

        await _sender.Received(1).Send(
            Arg.Is<ReviewDesignCommand>(c =>
                c.Request.DesignRevisionId == _v3.Id
                && !c.Request.Approved
                && c.Request.Notes == "Asked for version 1 back. The first linework was better."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskingForAVersionBack_WithoutANote_StillNamesTheVersion()
    {
        await AskBack(_v2.Id);

        await _sender.Received(1).Send(
            Arg.Is<ReviewDesignCommand>(c => c.Request.Notes == "Asked for version 2 back."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskingForTheLatestVersion_IsRefused()
    {
        Func<Task> act = () => AskBack(_v3.Id);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).WithMessage("*already the latest*");
        await _sender.DidNotReceive().Send(Arg.Any<ReviewDesignCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnceTheLatestIsApproved_AskingBackIsRefused()
    {
        _db.DesignApprovals.Add(new DesignApproval { StudioId = _studioId, DesignRevisionId = _v3.Id, Status = DesignApprovalStatus.Approved });
        _db.SaveChanges();

        Func<Task> act = () => AskBack(_v1.Id);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).WithMessage("*approved*");
    }

    [Fact]
    public async Task AnotherClient_OrStaff_CannotAskOnTheClientsBehalf()
    {
        Func<Task> otherClient = () => AskBack(_v1.Id, user: FakeCurrentUser.Client());
        Func<Task> owner = () => AskBack(_v1.Id, user: FakeCurrentUser.Owner());
        Func<Task> artist = () => AskBack(_v1.Id, user: new FakeCurrentUser(_artistUserId, "artist"));

        await otherClient.Should().ThrowAsync<NotFoundException>();
        await owner.Should().ThrowAsync<NotFoundException>();
        await artist.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void TheNoteIsBoundedSoThePrefixFitsTheReviewLimit()
    {
        RequestDesignVersionAgainValidator v = new();

        v.Validate(new RequestDesignVersionAgainCommand(_v1.Id, new string('x', 1900))).IsValid.Should().BeTrue();
        v.Validate(new RequestDesignVersionAgainCommand(_v1.Id, new string('x', 1901))).IsValid.Should().BeFalse();
    }

    // ── Bringing a version back ─────────────────────────────────────────────

    [Fact]
    public async Task BringingAVersionBack_UploadsTheSameFileAsTheNewestVersion()
    {
        await Restore(_v1.Id);

        await _sender.Received(1).Send(
            Arg.Is<UploadDesignRevisionCommand>(c =>
                c.Request.DesignId == _designId
                && c.Request.FileUrl == "https://r2.example.com/v1.png"
                && c.Request.Notes == "Version 1, brought back."),
            Arg.Any<CancellationToken>());
        _db.DesignRevisions.Single(r => r.Id == _v1.Id).FileUrl.Should().Be("https://r2.example.com/v1.png", "the old row is untouched");
    }

    [Fact]
    public async Task BringingBackTheLatest_IsRefused()
    {
        Func<Task> act = () => Restore(_v3.Id);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task BringingBackARevisionOfAnotherDesign_IsNotFound()
    {
        Func<Task> act = () => new RestoreDesignRevisionHandler(_db, _sender)
            .Handle(new RestoreDesignRevisionCommand(Guid.NewGuid(), _v1.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Version numbering ───────────────────────────────────────────────────

    [Fact]
    public async Task AfterAMiddleVersionIsDeleted_TheNextUploadIsStillAboveTheHighest()
    {
        // Removed outright so the fake store (no soft-delete filter) sees two rows, as the real filtered query
        // does: count + 1 gives 3, a duplicate; max + 1 gives 4.
        _db.DesignRevisions.Remove(_v2);
        _db.SaveChanges();

        DesignRevisionResponse r = await new UploadDesignRevisionHandler(
                _db, _tenant, Substitute.For<IRealtimeNotifier>(), Substitute.For<IJobScheduler>(), new FakeCurrentUser(_artistUserId, "artist"))
            .Handle(new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(_designId, "https://r2.example.com/v4.png", null)), default);

        r.VersionNumber.Should().Be(4, "count + 1 would have repeated version 3");
    }
}
