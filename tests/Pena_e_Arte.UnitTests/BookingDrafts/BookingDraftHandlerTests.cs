using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.BookingDrafts;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.BookingDrafts;

/// <summary>Saved booking drafts (S04): everything but the safety notes, seven days, the author only.</summary>
public class BookingDraftHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly FakeCurrentUser _client = FakeCurrentUser.Client();

    private ICurrentTenant Tenant()
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(_studioId);
        return tenant;
    }

    private const string Payload = """{"serviceId":"s-1","tattooDescription":"A rose","placement":["left_forearm"],"promoCode":"SPRING"}""";
    private static readonly List<BookingDraftImageRequest> Images =
        [new("https://media.tattooos.co/s/appointments/pending/u/AreaPhoto/a.jpg", "AreaPhoto")];

    private Task<BookingDraftResponse> Save(string payload, List<BookingDraftImageRequest>? images = null, FakeCurrentUser? as_ = null) =>
        new SaveMyBookingDraftHandler(_db, as_ ?? _client, Tenant(), TimeProvider.System)
            .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest(payload, images)), default);

    [Fact]
    public async Task Save_StoresThePayloadAndPhotos_ForSevenDays()
    {
        DateTime before = DateTime.UtcNow;
        BookingDraftResponse saved = await Save(Payload, Images);

        BookingDraft row = _db.BookingDrafts.Single();
        row.StudioId.Should().Be(_studioId);
        row.UserId.Should().Be(_client.UserId);
        row.PayloadJson.Should().Be(Payload);
        row.ExpiresAt.Should().BeCloseTo(before + TimeSpan.FromDays(7), TimeSpan.FromSeconds(5));
        _db.BookingDraftAttachments.Single().Category.Should().Be(AppointmentAttachmentCategory.AreaPhoto);
        saved.Images.Should().ContainSingle(i => i.Url == Images[0].Url);
    }

    [Fact]
    public async Task Save_Again_ReplacesTheDraft_AndRestartsItsSevenDays()
    {
        await Save(Payload, Images);
        BookingDraft first = _db.BookingDrafts.Single();
        first.ExpiresAt = DateTime.UtcNow.AddDays(1);
        await _db.SaveChangesAsync();

        await Save("""{"tattooDescription":"A peony"}""");

        BookingDraft row = _db.BookingDrafts.Single();
        row.PayloadJson.Should().Be("""{"tattooDescription":"A peony"}""");
        row.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
        _db.BookingDraftAttachments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"safetyNotes":"Allergic to latex"}""")]
    [InlineData("""{"safetyNotes":""}""")]
    [InlineData("""{"SafetyNotes":"x"}""")]
    [InlineData("""{"intake":{"safetyNotes":"x"}}""")]
    [InlineData("""{"list":[{"safetyNotes":"x"}]}""")]
    public async Task Save_RefusesAPayloadHoldingTheSafetyNotesKey_AndStoresNothing(string payload)
    {
        Func<Task> act = () => Save(payload);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .Which.Message.Should().Be(SaveMyBookingDraftHandler.ExcludedFieldMessage);
        _db.BookingDrafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ReturnsOnlyTheAuthorsOwnDraft()
    {
        FakeCurrentUser someoneElse = FakeCurrentUser.Client();
        await Save(Payload, as_: someoneElse);

        BookingDraftResponse? mine = await new GetMyBookingDraftHandler(_db, _client, TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default);
        BookingDraftResponse? theirs = await new GetMyBookingDraftHandler(_db, someoneElse, TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default);

        mine.Should().BeNull();
        theirs.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_AnExpiredDraft_ReadsAsNone()
    {
        await Save(Payload);
        _db.BookingDrafts.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        (await new GetMyBookingDraftHandler(_db, _client, TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheDraftAndItsPhotoReferences_AndIsIdempotent()
    {
        await Save(Payload, Images);
        DeleteMyBookingDraftHandler sut = new(_db, _client);

        await sut.Handle(new DeleteMyBookingDraftCommand(), default);
        await sut.Handle(new DeleteMyBookingDraftCommand(), default);

        _db.BookingDrafts.Should().BeEmpty();
        _db.BookingDraftAttachments.Should().BeEmpty();
    }

    [Fact]
    public void Rules_RecogniseTheKeyAtAnyDepth_ButNotSimilarNames()
    {
        BookingDraftRules.HoldsExcludedField("""{"notes":"x","safetyNotesLabel":"y"}""").Should().BeFalse();
        BookingDraftRules.HoldsExcludedField("""{"a":{"b":[{"safetyNotes":null}]}}""").Should().BeTrue();
        BookingDraftRules.IsJsonObject("[1,2]").Should().BeFalse();
        BookingDraftRules.IsJsonObject("not json").Should().BeFalse();
    }
}
