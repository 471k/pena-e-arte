using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Help.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Help;

public class LogHelpSearchHandlerTests
{
    private readonly FakeDbContext  _db     = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _user   = Substitute.For<ICurrentUser>();
    private readonly Guid           _studioId = Guid.NewGuid();
    private readonly Guid           _userId   = Guid.NewGuid();

    public LogHelpSearchHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.UserId.Returns(_userId);
        _user.Role.Returns("client");
    }

    private LogHelpSearchHandler CreateSut() => new(_db, _tenant, _user);

    [Fact]
    public async Task Handle_ValidRequest_CreatesAndPersistsLog()
    {
        LogHelpSearchCommand command = new(new LogHelpSearchRequest("book appointment", 3));

        await CreateSut().Handle(command, default);

        HelpSearchLog saved = _db.HelpSearchLogs.Single();
        saved.StudioId.Should().Be(_studioId);
        saved.UserId.Should().Be(_userId);
        saved.Role.Should().Be("client");
        saved.Query.Should().Be("book appointment");
        saved.ResultCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_QueryWithMixedCaseAndWhitespace_IsTrimmedAndLowercased()
    {
        LogHelpSearchCommand command = new(new LogHelpSearchRequest("  Book APPOINTMENT  ", 1));

        await CreateSut().Handle(command, default);

        _db.HelpSearchLogs.Single().Query.Should().Be("book appointment");
    }

    [Fact]
    public async Task Handle_ZeroResultQuery_PersistsResultCountZero()
    {
        LogHelpSearchCommand command = new(new LogHelpSearchRequest("xyzzy", 0));

        await CreateSut().Handle(command, default);

        _db.HelpSearchLogs.Single().ResultCount.Should().Be(0);
    }
}
