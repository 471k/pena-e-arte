using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class BookingDraftExpiryJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    [Fact]
    public async Task DeletesExpiredDrafts_WithTheirPhotoReferences_AndKeepsLiveOnes()
    {
        BookingDraft expired = new() { StudioId = Guid.NewGuid(), UserId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        expired.Attachments.Add(new BookingDraftAttachment { StudioId = expired.StudioId, DraftId = expired.Id, ImageUrl = "https://media.tattooos.co/a.jpg" });
        BookingDraft live = new() { StudioId = Guid.NewGuid(), UserId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(3) };
        _db.BookingDrafts.AddRange(expired, live);
        await _db.SaveChangesAsync();

        string outcome = await new BookingDraftExpiryJob(_db, NullLogger<BookingDraftExpiryJob>.Instance, TimeProvider.System).RunAsync();

        outcome.Should().Be("deleted 1 expired draft(s)");
        _db.BookingDrafts.Select(d => d.Id).Should().Equal(live.Id);
        _db.BookingDraftAttachments.Should().BeEmpty();
    }
}
