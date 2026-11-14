using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetAppointmentIcsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IExchangeRateProvider _rates = Substitute.For<IExchangeRateProvider>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetAppointmentIcsHandlerTests()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Test", Slug = "test", CountryCode = "AL", Currency = "EUR" });
        _db.SaveChanges();
        // Staff by default: the existing tests are about the file's content, not who may fetch it.
        _user.Role.Returns("owner");
        _user.UserId.Returns(Guid.NewGuid());
    }

    private GetAppointmentIcsHandler CreateSut() => new(_db, _rates, _user);

    private Client SignedInClient()
    {
        Client client = new() { StudioId = _studioId, UserId = Guid.NewGuid(), FirstName = "Cli", LastName = "Ent", Email = $"{Guid.NewGuid():N}@c.test" };
        _db.Clients.Add(client);
        _db.SaveChanges();
        _user.Role.Returns("client");
        _user.UserId.Returns(client.UserId!.Value);
        _user.Email.Returns(client.Email);
        return client;
    }

    private void LatestRate(decimal rate) =>
        _rates.GetLatestAsync("EUR", "ALL", Arg.Any<CancellationToken>())
              .Returns(new ExchangeRateSnapshot(rate, new DateOnly(2026, 10, 23), "BoA-Fix", false));

    private Guid SeedAppointment(bool includeArtist = true, Guid? clientId = null)
    {
        Artist? artist = includeArtist
            ? new Artist { StudioId = _studioId, FirstName = "Ink", LastName = "Master", Email = "ink@test.com" }
            : null;

        if (artist is not null) _db.Artists.Add(artist);

        Appointment appt = new()
        {
            StudioId = _studioId,
            ArtistId = artist?.Id ?? Guid.NewGuid(),
            ClientId = clientId ?? Guid.NewGuid(),
            Date = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Paid,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appt);
        if (artist is not null) appt.Artist = artist;
        _db.SaveChanges();
        return appt.Id;
    }

    // Ownership (14 Nov 2026): the endpoint is ClientAndAbove and used to load any appointment in the studio
    // by id, so one client could fetch another's file.
    [Fact]
    public async Task AClient_GetsNotFound_ForAnotherClientsAppointment_ExactlyAsForAMissingOne()
    {
        Client other = new() { StudioId = _studioId, FirstName = "Oth", LastName = "Er", Email = $"{Guid.NewGuid():N}@o.test" };
        _db.Clients.Add(other);
        _db.SaveChanges();
        Guid theirs = SeedAppointment(clientId: other.Id);
        SignedInClient();

        NotFoundException notMine = (await ((Func<Task>)(() => CreateSut().Handle(new GetAppointmentIcsQuery(theirs), default)))
            .Should().ThrowAsync<NotFoundException>()).Which;
        Guid missing = Guid.NewGuid();
        NotFoundException absent = (await ((Func<Task>)(() => CreateSut().Handle(new GetAppointmentIcsQuery(missing), default)))
            .Should().ThrowAsync<NotFoundException>()).Which;

        // The same answer as for an id that doesn't exist, apart from the id itself: nothing says it exists.
        notMine.Message.Replace(theirs.ToString(), "{id}").Should().Be(absent.Message.Replace(missing.ToString(), "{id}"));
    }

    [Fact]
    public async Task AClient_WithNoClientRecord_GetsNotFound()
    {
        Guid id = SeedAppointment();
        _user.Role.Returns("client");
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns((string?)null);

        await ((Func<Task>)(() => CreateSut().Handle(new GetAppointmentIcsQuery(id), default)))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AClient_GetsTheFile_ForTheirOwnAppointment()
    {
        Client me = SignedInClient();
        Guid mine = SeedAppointment(clientId: me.Id);

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(mine), default);

        ics.Should().Contain($"UID:{mine}@tattooos.co");
    }

    [Theory]
    [InlineData("artist")]
    [InlineData("owner")]
    [InlineData("admin")]
    public async Task Staff_GetTheFile_ForAnyAppointmentInTheirStudio(string role)
    {
        Guid id = SeedAppointment();
        _user.Role.Returns(role);

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(id), default);

        ics.Should().Contain($"UID:{id}@tattooos.co");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ReturnsIcsString()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().StartWith("BEGIN:VCALENDAR");
        ics.Should().Contain("END:VCALENDAR");
        ics.Should().Contain("BEGIN:VEVENT");
        ics.Should().Contain("END:VEVENT");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsCorrectDates()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DTSTART:20260915T100000Z");
        ics.Should().Contain("DTEND:20260915T120000Z");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsAppointmentUid()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain($"UID:{apptId}@tattooos.co");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsDepositInDescription()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 50.00 EUR");
    }

    [Fact]
    public async Task Handle_JpyStudioAppointment_FormatsDepositWithZeroDecimals()
    {
        Guid jpyStudioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = jpyStudioId, Name = "Tokyo", Slug = "tokyo", CountryCode = "JP", Currency = "JPY" });
        Appointment appt = new()
        {
            StudioId = jpyStudioId,
            ArtistId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Date = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Paid,
            DepositAmount = 3000m,
        };
        _db.Appointments.Add(appt);
        _db.SaveChanges();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(appt.Id), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 3,000 JPY");
    }

    [Fact]
    public async Task Handle_AlbanianEuroStudio_NotPaidYet_ShowsApproximateLekAtTheLatestRate()
    {
        LatestRate(90.74m);
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 50.00 EUR (≈ 4,537 ALL)");
    }

    [Fact]
    public async Task Handle_AlbanianEuroStudio_Paid_ShowsTheFrozenLekNotTheLatestRate()
    {
        LatestRate(95m);
        Guid apptId = SeedAppointment();
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = apptId,
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Currency = "EUR",
            Status = PaymentStatus.Paid,
            ReferenceCurrency = "ALL",
            ReferenceRate = 90.74m,
            ReferenceRateDate = new DateOnly(2026, 10, 23),
            ReferenceRateSource = "BoA-Fix",
            ReferenceAmount = 4537m,
        });
        _db.SaveChanges();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 50.00 EUR (4,537 ALL)");
    }

    [Fact]
    public async Task Handle_AlbanianLekStudio_IsUnchangedAndNeverReadsTheRate()
    {
        _db.Studios.Single(s => s.Id == _studioId).Currency = "ALL";
        _db.SaveChanges();
        LatestRate(90.74m);
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 50.00 ALL").And.NotContain("50.00 ALL (").And.NotContain("≈");
        await _rates.DidNotReceiveWithAnyArgs().GetLatestAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_UnknownAppointment_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetAppointmentIcsQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
