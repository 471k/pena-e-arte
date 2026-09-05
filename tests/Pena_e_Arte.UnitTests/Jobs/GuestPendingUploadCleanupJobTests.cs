using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class GuestPendingUploadCleanupJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    private GuestPendingUploadCleanupJob CreateSut() =>
        new(_db, _r2, NullLogger<GuestPendingUploadCleanupJob>.Instance);

    private const string Prefix = "appointments/guest-pending/";

    [Fact]
    public async Task RunAsync_UnreferencedObjectOlderThan48h_IsDeleted()
    {
        R2ObjectInfo orphan = new($"{Prefix}s1/area/orphan.png", DateTime.UtcNow.AddHours(-72), 1024);
        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([orphan]);
        _r2.GetPublicUrl(orphan.Key).Returns("https://cdn.example.com/" + orphan.Key);

        await CreateSut().RunAsync();

        await _r2.Received(1).DeleteAsync(orphan.Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObjectYoungerThan48h_IsNotDeleted()
    {
        R2ObjectInfo fresh = new($"{Prefix}s1/area/fresh.png", DateTime.UtcNow.AddHours(-1), 1024);
        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([fresh]);
        _r2.GetPublicUrl(fresh.Key).Returns("https://cdn.example.com/" + fresh.Key);

        await CreateSut().RunAsync();

        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObjectReferencedByAnAppointmentAttachment_IsNotDeleted()
    {
        string publicUrl = "https://cdn.example.com/" + Prefix + "s1/area/referenced.png";
        R2ObjectInfo referenced = new(Prefix + "s1/area/referenced.png", DateTime.UtcNow.AddHours(-72), 1024);
        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([referenced]);
        _r2.GetPublicUrl(referenced.Key).Returns(publicUrl);

        _db.AppointmentAttachments.Add(new AppointmentAttachment
        {
            StudioId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            ImageUrl = publicUrl,
        });
        await _db.SaveChangesAsync();

        await CreateSut().RunAsync();

        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MixOfOrphanedAndReferencedAndFresh_DeletesOnlyTheOrphan()
    {
        string referencedUrl = "https://cdn.example.com/" + Prefix + "s1/area/referenced.png";
        R2ObjectInfo orphan = new(Prefix + "s1/area/orphan.png", DateTime.UtcNow.AddHours(-72), 1024);
        R2ObjectInfo referenced = new(Prefix + "s1/area/referenced.png", DateTime.UtcNow.AddHours(-72), 1024);
        R2ObjectInfo fresh = new(Prefix + "s1/area/fresh.png", DateTime.UtcNow.AddHours(-1), 1024);

        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([orphan, referenced, fresh]);
        _r2.GetPublicUrl(orphan.Key).Returns("https://cdn.example.com/" + orphan.Key);
        _r2.GetPublicUrl(referenced.Key).Returns(referencedUrl);
        _r2.GetPublicUrl(fresh.Key).Returns("https://cdn.example.com/" + fresh.Key);

        _db.AppointmentAttachments.Add(new AppointmentAttachment
        {
            StudioId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            ImageUrl = referencedUrl,
        });
        await _db.SaveChangesAsync();

        await CreateSut().RunAsync();

        await _r2.Received(1).DeleteAsync(orphan.Key, Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().DeleteAsync(referenced.Key, Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().DeleteAsync(fresh.Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoObjectsUnderPrefix_DoesNotCallDelete()
    {
        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([]);

        await CreateSut().RunAsync();

        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OneDeleteFails_StillProcessesRemainingObjects()
    {
        R2ObjectInfo failing = new(Prefix + "s1/area/failing.png", DateTime.UtcNow.AddHours(-72), 1024);
        R2ObjectInfo succeeding = new(Prefix + "s1/area/succeeding.png", DateTime.UtcNow.AddHours(-72), 1024);
        _r2.ListByPrefixAsync(Prefix, Arg.Any<CancellationToken>()).Returns([failing, succeeding]);
        _r2.GetPublicUrl(failing.Key).Returns("https://cdn.example.com/" + failing.Key);
        _r2.GetPublicUrl(succeeding.Key).Returns("https://cdn.example.com/" + succeeding.Key);
        _r2.DeleteAsync(failing.Key, Arg.Any<CancellationToken>())
           .Returns<Task>(_ => throw new InvalidOperationException("storage unavailable"));

        Func<Task> act = () => CreateSut().RunAsync();

        await act.Should().NotThrowAsync();
        await _r2.Received(1).DeleteAsync(succeeding.Key, Arg.Any<CancellationToken>());
    }
}
