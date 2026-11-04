using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E04a Phase B (A03): GET /appointments/{id}/preparation gives the artist the same readiness facts as
/// the client, worded for the artist. It never tells them to do the client's action, and it is scoped to
/// their own assigned sessions.
/// </summary>
public class GetAppointmentPreparationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetAppointmentPreparationHandlerTests()
    {
        // The client detail read (used to compare against) needs the studio row.
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink Studio", Slug = _studioId.ToString("N")[..8], OwnerEmail = "owner@ink.test" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private Task<AppointmentPreparationResponse> Prepare(FakeCurrentUser user, Guid id) =>
        new GetAppointmentPreparationHandler(_db, user).Handle(new GetAppointmentPreparationQuery(id), default);

    [Fact]
    public async Task OutstandingItems_SayWhereTheyStand_AndWhatTheClientCanStillDo()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid id = (await SeedAppointment(await SeedService(true, true, true, true), artistId)).Id;

        AppointmentPreparationResponse prep = await Prepare(artist, id);

        prep.Items.Select(i => (i.Key, i.Label, i.Satisfied, i.State)).Should().Equal(
            ("DepositPaid", "Deposit", false, "Not paid yet"),
            ("IntakeForm", "Intake form", false, "Not submitted yet"),
            ("ConsentForm", "Consent form", false, "Not signed yet"),
            ("ReferenceImage", "Reference image", false, "None added"));
        prep.Items.Single(i => i.Key == "ConsentForm").FollowUp
            .Should().Be("The client can sign it from their appointment page.");
        prep.Items.Should().OnlyContain(i => i.FollowUp != null);
    }

    [Fact]
    public async Task NoArtistFacingString_TellsTheArtistToDoTheClientsAction()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid serviceId = await SeedService(true, true, true, true);
        (Guid card, _) = await SeedAppointment(serviceId, artistId);
        (Guid cash, Guid cashClient) = await SeedAppointment(serviceId, artistId);
        _db.Payments.Add(new Payment { StudioId = _studioId, AppointmentId = cash, ClientId = cashClient, Amount = 50m, Currency = "EUR", Status = PaymentStatus.CashPending, Method = ClientPaymentMethod.Cash });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<string> copy = [];
        foreach (Guid id in new[] { card, cash })
        {
            foreach (AppointmentPreparationItemResponse i in (await Prepare(artist, id)).Items)
                copy.AddRange(new[] { i.Label, i.State, i.FollowUp }.OfType<string>());
        }

        // The client's own instructions, as the client endpoint words them.
        string[] clientActions = ["Pay your deposit", "Fill in the intake form", "Sign the consent form", "Share a reference image"];
        copy.Should().NotContain(s => clientActions.Any(a => s.Contains(a, StringComparison.OrdinalIgnoreCase)));
        // No imperative client verb opening a line, and no second person: these are written about the client.
        copy.Should().NotContain(s => Regex.IsMatch(s, @"^(Sign|Fill|Pay|Share|Submit|Upload|Add)\b"));
        copy.Should().NotContain(s => Regex.IsMatch(s, @"\byour?\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task TheItems_AgreeWithTheClientsItemisedView()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid clientUserId = Guid.NewGuid();
        (Guid id, Guid clientId) = await SeedAppointment(await SeedService(true, true, true, true), artistId, clientUserId);
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = clientId, AppointmentId = id, SignedAt = DateTime.UtcNow });
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = id, ImageUrl = "https://x/r.jpg", Category = AppointmentAttachmentCategory.Reference });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        AppointmentPreparationResponse prep = await Prepare(artist, id);
        ICurrentUser client = Substitute.For<ICurrentUser>();
        client.Role.Returns("client");
        client.UserId.Returns(clientUserId);
        ClientAppointmentDetailResponse detail = await new GetMyAppointmentHandler(
                _db, client, Substitute.For<IIdentityService>(), Substitute.For<INotificationPreferenceService>())
            .Handle(new GetMyAppointmentQuery(id), default);

        prep.Items.Select(i => (i.Key, i.Satisfied)).Should().Equal(detail.Readiness.Select(r => (r.Key, r.Satisfied)));
        prep.Items.Single(i => i.Key == "ConsentForm").State.Should().Be("Signed");
        prep.Items.Where(i => i.Satisfied).Should().OnlyContain(i => i.FollowUp == null);
    }

    [Fact]
    public async Task NoRequirementsConfigured_GivesNoChecklist()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid id = (await SeedAppointment(await SeedService(), artistId)).Id;

        (await Prepare(artist, id)).Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task AFinishedSession_HasNothingToFollowUp(AppointmentStatus status)
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid id = (await SeedAppointment(await SeedService(true, true, true, true), artistId, status: status)).Id;

        AppointmentPreparationResponse prep = await Prepare(artist, id);

        prep.Items.Should().HaveCount(4).And.OnlyContain(i => !i.Satisfied && i.FollowUp == null);
    }

    [Fact]
    public async Task CashPending_IsNotReceived_AndTheDepositCarriesOnlyItsOutcomeFields()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        (Guid id, Guid clientId) = await SeedAppointment(await SeedService(deposit: true), artistId);
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = id,
            ClientId = clientId,
            Amount = 50m,
            Currency = "EUR",
            Status = PaymentStatus.CashPending,
            Method = ClientPaymentMethod.Cash,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        AppointmentPreparationResponse prep = await Prepare(artist, id);

        AppointmentPreparationItemResponse item = prep.Items.Single();
        item.Satisfied.Should().BeFalse();
        item.State.Should().Be("Cash not received yet");
        item.FollowUp.Should().Be("The client chose to pay in cash at the studio.");
        prep.Deposit.Should().Be(new AppointmentPreparationDepositResponse(
            50m, "CashPending", "Cash", null, "EUR", null, "Confirmed", null, null));
    }

    [Fact]
    public async Task NoPaymentRow_HasNoDeposit()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid artistId = await SeedArtist(artist.UserId);
        Guid id = (await SeedAppointment(await SeedService(deposit: true), artistId)).Id;

        (await Prepare(artist, id)).Deposit.Should().BeNull();
    }

    // ── Scope ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherArtistsSession_IsNotFound()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        await SeedArtist(artist.UserId);
        Guid otherArtistId = await SeedArtist(Guid.NewGuid());
        Guid id = (await SeedAppointment(await SeedService(consent: true), otherArtistId)).Id;

        Func<Task> act = () => Prepare(artist, id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AnUnassignedRequest_IsNotFound_ForAnArtist()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        await SeedArtist(artist.UserId);
        Guid id = (await SeedAppointment(await SeedService(consent: true), artistId: null)).Id;

        Func<Task> act = () => Prepare(artist, id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AnArtistWithNoProfile_IsNotFound()
    {
        Guid id = (await SeedAppointment(await SeedService(consent: true), artistId: null)).Id;

        Func<Task> act = () => Prepare(FakeCurrentUser.Artist(), id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AnOwner_CanReadAnySessionInTheStudio()
    {
        Guid someArtist = await SeedArtist(Guid.NewGuid());
        Guid id = (await SeedAppointment(await SeedService(consent: true), someArtist)).Id;

        (await Prepare(FakeCurrentUser.Owner(), id)).Items.Should().ContainSingle(i => i.Key == "ConsentForm");
    }

    [Fact]
    public async Task AMissingAppointment_IsNotFound()
    {
        Func<Task> act = () => Prepare(FakeCurrentUser.Owner(), Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void TheValidator_RejectsAnEmptyId()
    {
        new GetAppointmentPreparationValidator().Validate(new GetAppointmentPreparationQuery(Guid.Empty))
            .IsValid.Should().BeFalse();
    }

    // ── Seeding ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedArtist(Guid userId)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Ari",
            LastName = "Kola",
            Email = $"{Guid.NewGuid():N}@a.test",
            IsActive = true,
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedService(
        bool deposit = false, bool intake = false, bool consent = false, bool reference = false)
    {
        Service service = new()
        {
            StudioId = _studioId,
            Name = "Session",
            DurationMinutes = 120,
            IsActive = true,
            RequiresDepositPaid = deposit,
            RequiresIntakeForm = intake,
            RequiresConsentForm = consent,
            RequiresReferenceImage = reference,
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<(Guid Id, Guid ClientId)> SeedAppointment(
        Guid serviceId, Guid? artistId, Guid? clientUserId = null,
        AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = clientUserId,
            FirstName = "Cli",
            LastName = "Ent",
            Email = $"{Guid.NewGuid():N}@c.test",
        };
        _db.Clients.Add(client);
        DateTime start = DateTime.UtcNow.AddDays(3);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = client.Id,
            ArtistId = artistId,
            ServiceId = serviceId,
            Date = start,
            EndDate = start.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (appointment.Id, client.Id);
    }
}
