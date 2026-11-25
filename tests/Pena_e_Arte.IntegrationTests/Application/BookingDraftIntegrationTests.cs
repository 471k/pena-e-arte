using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Saved booking drafts on the real, filter-configured context. FakeDbContext applies no query filters, so only
/// this shows a draft is scoped to its studio, and that the sweep's reader still sees its photos under a job.
/// </summary>
[Collection("Database")]
public class BookingDraftIntegrationTests(DatabaseFixture fixture)
{
    private const string Address = "https://media.example.com";

    private sealed record Caller(Guid UserId) : ICurrentUser
    {
        public string Role => "client";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
    }

    private static ICurrentTenant Tenant(Guid studioId)
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(studioId);
        return tenant;
    }

    [Fact]
    public async Task OnePersonsDraftsAtTwoStudios_EachStudioSeesOnlyItsOwn()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        Caller me = new(Guid.NewGuid());

        await using (AppDbContext a = fixture.CreateDbContext(studioA))
            await new SaveMyBookingDraftHandler(a, me, Tenant(studioA), TimeProvider.System)
                .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest("""{"tattooDescription":"at A"}""", null)), default);
        await using (AppDbContext b = fixture.CreateDbContext(studioB))
            await new SaveMyBookingDraftHandler(b, me, Tenant(studioB), TimeProvider.System)
                .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest("""{"tattooDescription":"at B"}""", null)), default);

        await using AppDbContext readA = fixture.CreateDbContext(studioA);
        BookingDraftResponse? atA = await new GetMyBookingDraftHandler(readA, me, TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default);

        atA.Should().NotBeNull();
        atA!.Payload.Should().Be("""{"tattooDescription":"at A"}""");
        atA.Payload.Should().NotContain("safetyNotes");

        // Both studios, so a missing filter can't pass by returning the right row by chance.
        await using AppDbContext readB = fixture.CreateDbContext(studioB);
        BookingDraftResponse? atB = await new GetMyBookingDraftHandler(readB, me, TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default);
        atB!.Payload.Should().Be("""{"tattooDescription":"at B"}""");
    }

    [Fact]
    public async Task TheSweepsReader_UnderJobConditions_StillProtectsADraftsPhoto()
    {
        Guid studio = Guid.NewGuid();
        Caller me = new(Guid.NewGuid());
        string key = $"{studio}/appointments/pending/u/AreaPhoto/{Guid.NewGuid()}.jpg";
        await using (AppDbContext seed = fixture.CreateDbContext(studio))
            await new SaveMyBookingDraftHandler(seed, me, Tenant(studio), TimeProvider.System)
                .Handle(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest("{}", [new($"{Address}/{key}", "AreaPhoto")])), default);

        await using AppDbContext job = fixture.CreateDbContext(Guid.Empty);
        R2Service r2 = new(Substitute.For<IAmazonS3>(), Options.Create(new R2Options { BucketName = "test-bucket", PublicUrl = Address }));
        StoredObjectReferences refs = await new StoredObjectReferenceReader(job, r2).ReadAsync(default);

        refs.IsReferenced(key).Should().BeTrue(because: "a live draft's photo must not look like an orphan");
    }
}
