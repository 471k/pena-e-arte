using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateMyClientHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private UpdateMyClientHandler CreateSut() => new(_db, _currentUser);

    private static UpdateMyClientCommand Command(
        string firstName = "Ana", string lastName = "Silva", string? phone = "+355691234567") =>
        new(new UpdateMyClientRequest(firstName, lastName, phone));

    [Fact]
    public async Task Handle_UpdatesNameAndPhone_OnCallersClient()
    {
        Guid myUserId = Guid.NewGuid();
        Client client = await SeedClient(myUserId);
        _currentUser.UserId.Returns(myUserId);

        ClientResponse result =
            await CreateSut().Handle(Command("Ana", "Silva", "+355691234567"), default);

        Client saved = _db.Clients.Single(c => c.Id == client.Id);
        saved.FirstName.Should().Be("Ana");
        saved.LastName.Should().Be("Silva");
        saved.Phone.Should().Be("+355691234567");
        result.FirstName.Should().Be("Ana");
        result.Phone.Should().Be("+355691234567");
    }

    [Fact]
    public async Task Handle_NeverChangesEmail()
    {
        Guid myUserId = Guid.NewGuid();
        Client client = await SeedClient(myUserId, email: "keep@test.com");
        _currentUser.UserId.Returns(myUserId);

        await CreateSut().Handle(Command(), default);

        _db.Clients.Single(c => c.Id == client.Id).Email.Should().Be("keep@test.com");
    }

    [Fact]
    public async Task Handle_TrimsWhitespace()
    {
        Guid myUserId = Guid.NewGuid();
        Client client = await SeedClient(myUserId);
        _currentUser.UserId.Returns(myUserId);

        await CreateSut().Handle(Command("  Ana ", " Silva  ", " +355691234567 "), default);

        Client saved = _db.Clients.Single(c => c.Id == client.Id);
        saved.FirstName.Should().Be("Ana");
        saved.LastName.Should().Be("Silva");
        saved.Phone.Should().Be("+355691234567");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_BlankPhone_ClearsTheNumber(string? phone)
    {
        Guid myUserId = Guid.NewGuid();
        Client client = await SeedClient(myUserId, phone: "+355690000000");
        _currentUser.UserId.Returns(myUserId);

        await CreateSut().Handle(Command(phone: phone), default);

        _db.Clients.Single(c => c.Id == client.Id).Phone.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CallerBelongsToTwoStudios_UpdatesBothRows()
    {
        Guid myUserId = Guid.NewGuid();
        Client clientA = await SeedClient(myUserId, Guid.NewGuid(), phone: "+355690000001");
        Client clientB = await SeedClient(myUserId, Guid.NewGuid(), phone: "+355690000002");
        _currentUser.UserId.Returns(myUserId);

        UpdateMyClientCommand command = Command("Ana", "Silva", "+355691234567");
        await CreateSut().Handle(command, default);

        foreach (Guid id in new[] { clientA.Id, clientB.Id })
        {
            Client saved = _db.Clients.Single(c => c.Id == id);
            saved.FirstName.Should().Be("Ana");
            saved.LastName.Should().Be("Silva");
            saved.Phone.Should().Be("+355691234567");
        }

        command.AffectedClientCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NeverTouchesAnotherPersonsClientRow()
    {
        Guid myUserId = Guid.NewGuid();
        await SeedClient(myUserId);
        Client other = await SeedClient(Guid.NewGuid(), firstName: "Other", phone: "+355690000009");
        _currentUser.UserId.Returns(myUserId);

        await CreateSut().Handle(Command("Ana", "Silva", "+355691234567"), default);

        Client untouched = _db.Clients.Single(c => c.Id == other.Id);
        untouched.FirstName.Should().Be("Other");
        untouched.Phone.Should().Be("+355690000009");
    }

    [Fact]
    public async Task Handle_SetsResolvedClientId_ForAudit()
    {
        Guid myUserId = Guid.NewGuid();
        Client client = await SeedClient(myUserId);
        _currentUser.UserId.Returns(myUserId);

        UpdateMyClientCommand command = Command();
        await CreateSut().Handle(command, default);

        command.AuditTargetId.Should().Be(client.Id);
        command.AffectedClientCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoClientForCaller_ThrowsNotFound()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(Command(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Client> SeedClient(
        Guid userId,
        Guid? studioId = null,
        string firstName = "Old",
        string email = "client@test.com",
        string? phone = null)
    {
        Client client = new()
        {
            StudioId = studioId ?? Guid.NewGuid(),
            UserId = userId,
            FirstName = firstName,
            LastName = "Name",
            Email = email,
            Phone = phone,
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}

public class UpdateMyClientValidatorTests
{
    private readonly UpdateMyClientValidator _validator = new();

    private static UpdateMyClientCommand Command(string first, string last, string? phone) =>
        new(new UpdateMyClientRequest(first, last, phone));

    [Theory]
    [InlineData("Ana", "Silva", "+355691234567")]
    [InlineData("Ana", "Silva", null)]
    [InlineData("Ana", "Silva", "")]
    [InlineData("Ana", "Silva", "   ")]
    public void Validate_ValidInput_Passes(string first, string last, string? phone) =>
        _validator.Validate(Command(first, last, phone)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("", "Silva")]
    [InlineData("Ana", "")]
    [InlineData("   ", "Silva")]
    public void Validate_EmptyName_Fails(string first, string last) =>
        _validator.Validate(Command(first, last, null)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NameTooLong_Fails() =>
        _validator.Validate(Command(new string('a', 101), "Silva", null)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("0691234567")]
    [InlineData("+0691234567")]
    [InlineData("+355 69 123 4567")]
    [InlineData("call me")]
    public void Validate_NonE164Phone_FailsWithStandardMessage(string phone)
    {
        ValidationResult result = _validator.Validate(Command("Ana", "Silva", phone));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == PhoneValidationRules.E164ErrorMessage);
    }
}
