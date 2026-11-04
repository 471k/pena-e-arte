using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E04a Phase A: the staff list (GET /appointments) carries the DEC03 readiness summary from the same
/// derivation the client's reads use, the brief and placement the Today view shows, never SafetyNotes,
/// and an "assigned to me" scope that gives an owner who is also an artist their own work only.
/// </summary>
public class StaffAppointmentReadinessTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private Task<List<AppointmentResponse>> List(FakeCurrentUser user, bool assignedToMe = false, Guid? artistId = null) =>
        new GetAppointmentsHandler(_db, user).Handle(new GetAppointmentsQuery(null, null, artistId, assignedToMe), default);

    // ── Readiness summary per requirement configuration ────────────────────────

    [Fact]
    public async Task AllFourRequirements_NothingDone_IsFourRequiredNoneSatisfied()
    {
        Guid serviceId = await SeedService(consent: true, intake: true, reference: true, deposit: true);
        Guid id = await SeedAppointment(serviceId: serviceId);

        AppointmentResponse row = (await List(FakeCurrentUser.Owner())).Single(a => a.Id == id);

        row.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 0));
    }

    [Fact]
    public async Task EachRequirementOnItsOwn_CountsOnlyThatOne_AndFollowsTheRealRow()
    {
        Guid consentService = await SeedService(consent: true);
        Guid intakeService = await SeedService(intake: true);
        Guid referenceService = await SeedService(reference: true);
        Guid depositService = await SeedService(deposit: true);
        (Guid consent, Guid consentClient) = await SeedAppointmentWithClient(consentService);
        (Guid intake, Guid intakeClient) = await SeedAppointmentWithClient(intakeService);
        Guid reference = await SeedAppointment(serviceId: referenceService);
        (Guid deposit, Guid depositClient) = await SeedAppointmentWithClient(depositService);

        Dictionary<Guid, AppointmentReadinessSummaryResponse?> before =
            (await List(FakeCurrentUser.Owner())).ToDictionary(a => a.Id, a => a.Readiness);
        before[consent].Should().Be(new AppointmentReadinessSummaryResponse(1, 0));
        before[intake].Should().Be(new AppointmentReadinessSummaryResponse(1, 0));
        before[reference].Should().Be(new AppointmentReadinessSummaryResponse(1, 0));
        before[deposit].Should().Be(new AppointmentReadinessSummaryResponse(1, 0));

        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = consentClient, AppointmentId = consent, SignedAt = DateTime.UtcNow });
        _db.IntakeForms.Add(new IntakeForm { StudioId = _studioId, ClientId = intakeClient, AppointmentId = intake, SubmittedAt = DateTime.UtcNow, FormData = "{}" });
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = reference, ImageUrl = "https://x/ref.jpg", Category = AppointmentAttachmentCategory.Reference });
        _db.Payments.Add(new Payment { StudioId = _studioId, AppointmentId = deposit, ClientId = depositClient, Amount = 50m, Currency = "EUR", Status = PaymentStatus.Paid });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await List(FakeCurrentUser.Owner())).Should()
            .OnlyContain(a => a.Readiness == new AppointmentReadinessSummaryResponse(1, 1));
    }

    [Fact]
    public async Task NoRequirementsConfigured_IsTheEmptySummary_NotAChecklistOfZeroes()
    {
        Guid bareService = await SeedService();
        Guid withBareService = await SeedAppointment(serviceId: bareService);
        Guid withNoService = await SeedAppointment();

        List<AppointmentResponse> rows = await List(FakeCurrentUser.Owner());

        // Required = 0 is the contract's one "nothing configured" value (AppointmentReadinessSummaryResponse);
        // the Today view renders no preparation line for it, and the itemised list is empty.
        rows.Single(a => a.Id == withBareService).Readiness!.Required.Should().Be(0);
        rows.Single(a => a.Id == withNoService).Readiness!.Required.Should().Be(0);
        rows.Should().OnlyContain(a => a.Readiness!.Satisfied == 0);
    }

    // ── Scope ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArtistCaller_GetsOnlyTheirOwnAssignedSessions()
    {
        FakeCurrentUser artist = FakeCurrentUser.Artist();
        Guid mine = await SeedArtist(artist.UserId);
        Guid someoneElse = await SeedArtist(Guid.NewGuid());
        Guid myAppointment = await SeedAppointment(artistId: mine);
        await SeedAppointment(artistId: someoneElse);
        await SeedAppointment(artistId: null);

        List<AppointmentResponse> rows = await List(artist, assignedToMe: true);

        rows.Select(a => a.Id).Should().Equal(myAppointment);
    }

    [Fact]
    public async Task OwnerWhoIsAlsoAnArtist_AssignedToMe_SeesTheirOwnWork_NotTheWholeStudio()
    {
        FakeCurrentUser owner = FakeCurrentUser.Owner();
        Guid ownersArtistProfile = await SeedArtist(owner.UserId);
        Guid otherArtist = await SeedArtist(Guid.NewGuid());
        Guid ownersSession = await SeedAppointment(artistId: ownersArtistProfile);
        await SeedAppointment(artistId: otherArtist);
        await SeedAppointment(artistId: null); // an unassigned request is the owner's E05 view, not Today

        (await List(owner, assignedToMe: true)).Select(a => a.Id).Should().Equal(ownersSession);
        // Without the scope the owner still sees the whole studio, as before.
        (await List(owner)).Should().HaveCount(3);
    }

    [Fact]
    public async Task AssignedToMe_IgnoresARequestedArtistId()
    {
        FakeCurrentUser owner = FakeCurrentUser.Owner();
        Guid ownersArtistProfile = await SeedArtist(owner.UserId);
        Guid otherArtist = await SeedArtist(Guid.NewGuid());
        Guid ownersSession = await SeedAppointment(artistId: ownersArtistProfile);
        await SeedAppointment(artistId: otherArtist);

        (await List(owner, assignedToMe: true, artistId: otherArtist)).Select(a => a.Id).Should().Equal(ownersSession);
    }

    [Fact]
    public async Task NoArtistProfile_HasNoAssignedSessions_RatherThanEveryUnassignedRequest()
    {
        await SeedAppointment(artistId: null);
        await SeedAppointment(artistId: await SeedArtist(Guid.NewGuid()));

        (await List(FakeCurrentUser.Owner(), assignedToMe: true)).Should().BeEmpty();
        (await List(FakeCurrentUser.Artist())).Should().BeEmpty();
    }

    // ── What a row carries ─────────────────────────────────────────────────────

    [Fact]
    public async Task ARow_CarriesTheBriefPlacementAndClientLogin_ButNeverSafetyNotes()
    {
        Guid clientUserId = Guid.NewGuid();
        (Guid id, _) = await SeedAppointmentWithClient(clientUserId: clientUserId);
        _db.BookingIntakes.Add(new BookingIntake
        {
            StudioId = _studioId,
            AppointmentId = id,
            TattooDescription = "A swallow on the forearm",
            SafetyNotes = "medical detail",
            DesiredPlacement = new() { Locations = ["left-forearm"] },
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        AppointmentResponse row = (await List(FakeCurrentUser.Owner())).Single();

        row.TattooDescription.Should().Be("A swallow on the forearm");
        row.DesiredPlacementLocations.Should().Equal("left-forearm");
        row.ClientUserId.Should().Be(clientUserId);
        row.SafetyNotes.Should().BeNull();
    }

    [Fact]
    public async Task TheStaffAppointmentPage_CarriesTheBriefAndSafetyNotes()
    {
        (Guid id, _) = await SeedAppointmentWithClient();
        _db.BookingIntakes.Add(new BookingIntake
        {
            StudioId = _studioId,
            AppointmentId = id,
            TattooDescription = "A swallow on the forearm",
            SafetyNotes = "medical detail",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        AppointmentResponse detail = await new GetAppointmentHandler(_db).Handle(new GetAppointmentQuery(id), default);

        detail.TattooDescription.Should().Be("A swallow on the forearm");
        detail.SafetyNotes.Should().Be("medical detail");
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
        bool consent = false, bool intake = false, bool reference = false, bool deposit = false)
    {
        Service service = new()
        {
            StudioId = _studioId,
            Name = "Session",
            DurationMinutes = 120,
            IsActive = true,
            RequiresConsentForm = consent,
            RequiresIntakeForm = intake,
            RequiresReferenceImage = reference,
            RequiresDepositPaid = deposit,
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<Guid> SeedAppointment(Guid? serviceId = null, Guid? artistId = null) =>
        (await SeedAppointmentWithClient(serviceId, artistId)).AppointmentId;

    private async Task<(Guid AppointmentId, Guid ClientId)> SeedAppointmentWithClient(
        Guid? serviceId = null, Guid? artistId = null, Guid? clientUserId = null)
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
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (appointment.Id, client.Id);
    }
}
