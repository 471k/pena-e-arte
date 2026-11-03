using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// GET /appointments/mine/{id} (DEC02): the caller's own appointment only (404, never 403, for anyone
/// else's), with SelfService and the itemised readiness derived from real rows, and a summary that
/// agrees with what /mine says for the same appointment.
/// </summary>
public class GetMyAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetMyAppointmentHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _currentUser.Role.Returns("client");
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Ink Studio",
            Slug = _studioId.ToString("N")[..8],
            OwnerEmail = "owner@ink.test",
            PhoneNumber = "+355 69 111 2222",
            InstagramHandle = "inkstudio",
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private Task<ClientAppointmentDetailResponse> Get(Guid id) =>
        new GetMyAppointmentHandler(_db, _currentUser, _identity).Handle(new GetMyAppointmentQuery(id), default);

    private Task<List<AppointmentResponse>> Mine() =>
        new GetMyAppointmentsHandler(_db, _currentUser).Handle(new GetMyAppointmentsQuery(), default);

    [Fact]
    public async Task OwnAppointment_ReturnsIt_WithSelfService()
    {
        Guid clientId = await SeedClient(_userId);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));

        ClientAppointmentDetailResponse result = await Get(id);

        result.Appointment.Id.Should().Be(id);
        result.Appointment.SelfService.Should()
            .Be(new AppointmentSelfServiceResponse(AppointmentSelfServiceDefaults.CancellationWindowHours, true));
    }

    [Fact]
    public async Task AnotherClientsAppointment_IsNotFound_NotForbidden()
    {
        await SeedClient(_userId);
        Guid otherClientId = await SeedClient(Guid.NewGuid());
        Guid id = await SeedAppointment(otherClientId, DateTime.UtcNow.AddDays(5));

        Func<Task> act = () => Get(id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MissingAppointment_IsNotFound()
    {
        await SeedClient(_userId);

        Func<Task> act = () => Get(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CallerWithNoClientRecord_IsNotFound()
    {
        Guid clientId = await SeedClient(Guid.NewGuid());
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));

        Func<Task> act = () => Get(id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SafetyNotes_AreWithheld()
    {
        Guid clientId = await SeedClient(_userId);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));
        _db.BookingIntakes.Add(new BookingIntake
        {
            StudioId = _studioId,
            AppointmentId = id,
            TattooDescription = "A swallow on the forearm",
            SafetyNotes = "medical detail",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        ClientAppointmentDetailResponse result = await Get(id);

        result.Appointment.TattooDescription.Should().Be("A swallow on the forearm");
        result.Appointment.SafetyNotes.Should().BeNull();
    }

    [Fact]
    public async Task NoRequirementsConfigured_ReadinessIsEmpty_AndTheSummaryIsZero()
    {
        Guid clientId = await SeedClient(_userId);
        Guid serviceId = await SeedService();
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), serviceId);

        ClientAppointmentDetailResponse result = await Get(id);

        result.Readiness.Should().BeEmpty();
        result.Appointment.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(0, 0));
    }

    [Fact]
    public async Task Readiness_ReflectsRealFormAttachmentAndDepositRows()
    {
        Guid clientId = await SeedClient(_userId);
        Guid serviceId = await SeedService(consent: true, intake: true, reference: true, deposit: true);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), serviceId);

        ClientAppointmentDetailResponse before = await Get(id);
        before.Readiness.Select(r => (r.Key, r.Satisfied)).Should().Equal(
            ("DepositPaid", false), ("IntakeForm", false), ("ConsentForm", false), ("ReferenceImage", false));
        before.Readiness.Single(r => r.Key == "ConsentForm").Destination
            .Should().Be($"/forms/consent/new?appointmentId={id}");
        before.Readiness.Single(r => r.Key == "IntakeForm").Destination
            .Should().Be($"/forms/intake/new?appointmentId={id}");
        before.Readiness.Single(r => r.Key == "DepositPaid").Destination.Should().BeNull();
        before.Readiness.Single(r => r.Key == "ReferenceImage").Available.Should().BeFalse();
        before.Readiness.Single(r => r.Key == "ReferenceImage").Note.Should().NotBeNullOrEmpty();

        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = clientId, AppointmentId = id, SignedAt = DateTime.UtcNow });
        _db.IntakeForms.Add(new IntakeForm { StudioId = _studioId, ClientId = clientId, AppointmentId = id, SubmittedAt = DateTime.UtcNow, FormData = "{}" });
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = id, ImageUrl = "https://x/y.jpg", Category = AppointmentAttachmentCategory.Reference });
        _db.Payments.Add(new Payment { StudioId = _studioId, AppointmentId = id, ClientId = clientId, Amount = 50m, Currency = "EUR", Status = PaymentStatus.Paid });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        ClientAppointmentDetailResponse after = await Get(id);
        after.Readiness.Should().OnlyContain(r => r.Satisfied && !r.Available && r.Destination == null);
        after.Appointment.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 4));
    }

    [Fact]
    public async Task AnAreaPhotoIsNotAReferenceImage_AndAnUnsignedConsentDoesNotCount()
    {
        Guid clientId = await SeedClient(_userId);
        Guid serviceId = await SeedService(consent: true, reference: true);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), serviceId);
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = clientId, AppointmentId = id, SignedAt = null });
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = id, ImageUrl = "https://x/a.jpg", Category = AppointmentAttachmentCategory.AreaPhoto });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        ClientAppointmentDetailResponse result = await Get(id);

        result.Readiness.Should().OnlyContain(r => !r.Satisfied);
    }

    [Fact]
    public async Task CashPending_DoesNotSatisfyTheDeposit_AndSaysWhy()
    {
        Guid clientId = await SeedClient(_userId);
        Guid serviceId = await SeedService(deposit: true);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), serviceId);
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

        AppointmentReadinessItemResponse deposit = (await Get(id)).Readiness.Single();

        deposit.Satisfied.Should().BeFalse();
        deposit.Available.Should().BeFalse();
        deposit.Note.Should().Contain("cash");
    }

    [Fact]
    public async Task TheListSummary_AgreesWithTheDetail_ForEveryAppointment()
    {
        Guid clientId = await SeedClient(_userId);
        Guid serviceId = await SeedService(consent: true, intake: true);
        Guid signed = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), serviceId);
        Guid unsigned = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(6), serviceId);
        Guid noService = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(7));
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = clientId, AppointmentId = signed, SignedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<AppointmentResponse> list = await Mine();

        foreach (Guid id in new[] { signed, unsigned, noService })
        {
            ClientAppointmentDetailResponse detail = await Get(id);
            list.Single(a => a.Id == id).Readiness.Should().Be(detail.Appointment.Readiness);
            detail.Appointment.Readiness!.Required.Should().Be(detail.Readiness.Count);
            detail.Appointment.Readiness.Satisfied.Should().Be(detail.Readiness.Count(r => r.Satisfied));
        }
        list.Single(a => a.Id == signed).Readiness.Should().Be(new AppointmentReadinessSummaryResponse(2, 1));
        list.Single(a => a.Id == noService).Readiness.Should().Be(new AppointmentReadinessSummaryResponse(0, 0));
    }

    // ── Contact (DEC06) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Contact_AssignedArtistWithALogin_IsTheRecipient()
    {
        Guid clientId = await SeedClient(_userId);
        Guid artistUserId = Guid.NewGuid();
        Guid artistId = await SeedArtist(artistUserId, isActive: true);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), artistId: artistId);

        ClientAppointmentContactResponse contact = (await Get(id)).Contact;

        contact.RecipientUserId.Should().Be(artistUserId);
        contact.RecipientRole.Should().Be("artist");
        contact.RecipientName.Should().Be("Ari Kola");
        await _identity.DidNotReceiveWithAnyArgs().GetStudioOwnerUserIdAsync(default, default);
    }

    [Fact]
    public async Task Contact_NoArtist_IsTheDeclaredOwner()
    {
        Guid clientId = await SeedClient(_userId);
        Guid ownerUserId = Guid.NewGuid();
        _identity.GetUserIdByEmailAsync("owner@ink.test", Arg.Any<CancellationToken>()).Returns(ownerUserId);
        _identity.GetUserDisplayNameAsync("owner@ink.test", Arg.Any<CancellationToken>()).Returns("Dua");
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));

        ClientAppointmentContactResponse contact = (await Get(id)).Contact;

        contact.RecipientUserId.Should().Be(ownerUserId);
        contact.RecipientRole.Should().Be("owner");
        contact.RecipientName.Should().Be("Dua");
    }

    [Theory]
    [InlineData(false, true)]   // artist without a login can't read a message
    [InlineData(true, false)]   // deactivated artist isn't messageable
    public async Task Contact_ArtistWhoCantBeMessaged_FallsBackToTheOwner(bool hasLogin, bool isActive)
    {
        Guid clientId = await SeedClient(_userId);
        Guid artistId = await SeedArtist(hasLogin ? Guid.NewGuid() : null, isActive);
        Guid ownerUserId = Guid.NewGuid();
        _identity.GetUserIdByEmailAsync("owner@ink.test", Arg.Any<CancellationToken>()).Returns(ownerUserId);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), artistId: artistId);

        ClientAppointmentContactResponse contact = (await Get(id)).Contact;

        contact.RecipientUserId.Should().Be(ownerUserId);
        contact.RecipientRole.Should().Be("owner");
    }

    [Fact]
    public async Task Contact_NoArtistAndNoOwner_HasNoRecipient_ButTheStudiosOwnDetails()
    {
        Guid clientId = await SeedClient(_userId);
        _identity.GetUserIdByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.GetStudioOwnerUserIdAsync(_studioId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));

        ClientAppointmentContactResponse contact = (await Get(id)).Contact;

        contact.RecipientUserId.Should().BeNull();
        contact.RecipientRole.Should().BeNull();
        contact.StudioName.Should().Be("Ink Studio");
        contact.StudioPhone.Should().Be("+355 69 111 2222");
        contact.StudioInstagram.Should().Be("inkstudio");
    }

    [Fact]
    public async Task Contact_SoloStudio_OwnerWhoIsAlsoTheArtist_IsOneRecipient()
    {
        Guid clientId = await SeedClient(_userId);
        Guid soloUserId = Guid.NewGuid();
        Guid artistId = await SeedArtist(soloUserId, isActive: true);
        _identity.GetUserIdByEmailAsync("owner@ink.test", Arg.Any<CancellationToken>()).Returns(soloUserId);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5), artistId: artistId);

        ClientAppointmentContactResponse contact = (await Get(id)).Contact;

        contact.RecipientUserId.Should().Be(soloUserId);
        contact.RecipientRole.Should().Be("artist");
    }

    private async Task<Guid> SeedArtist(Guid? userId, bool isActive)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            IsActive = isActive,
            FirstName = "Ari",
            LastName = "Kola",
            Email = $"{Guid.NewGuid():N}@a.test",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return artist.Id;
    }

    private async Task<Guid> SeedClient(Guid userId)
    {
        Client client = new() { StudioId = _studioId, UserId = userId, FirstName = "T", LastName = "C", Email = $"{Guid.NewGuid():N}@c.com" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
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
        _db.ChangeTracker.Clear();
        return service.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId, DateTime date, Guid? serviceId = null, Guid? artistId = null)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = clientId,
            ArtistId = artistId,
            ServiceId = serviceId,
            Date = date,
            EndDate = date.AddMinutes(120),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }
}
