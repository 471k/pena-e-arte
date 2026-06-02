using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class CreateClientHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public CreateClientHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateClientHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NewEmail_ReturnsClientResponse()
    {
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", "+351911000000");

        ClientResponse result = await CreateSut().Handle(new CreateClientCommand(req), default);

        result.FirstName.Should().Be("Ana");
        result.LastName.Should().Be("Costa");
        result.Email.Should().Be("ana@example.com");
        result.Phone.Should().Be("+351911000000");
        result.StudioId.Should().Be(_studioId);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_NewEmail_PersistsClientToDb()
    {
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null);

        await CreateSut().Handle(new CreateClientCommand(req), default);

        _db.Clients.Should().ContainSingle(c => c.Email == "ana@example.com");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsBusinessRuleViolationException()
    {
        const string email = "duplicate@example.com";
        _db.Clients.Add(new Client
        {
            StudioId  = _studioId,
            FirstName = "Existing",
            LastName  = "Client",
            Email     = email
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateClientCommand(new("New", "Client", email, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage($"*{email}*");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_DoesNotPersistSecondClient()
    {
        const string email = "duplicate@example.com";
        _db.Clients.Add(new Client { StudioId = _studioId, FirstName = "A", LastName = "B", Email = email });
        await _db.SaveChangesAsync();

        try { await CreateSut().Handle(new CreateClientCommand(new("C", "D", email, null)), default); } catch { }

        _db.Clients.Should().ContainSingle(c => c.Email == email);
    }
}
