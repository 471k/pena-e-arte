using System.Text.Json;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Common;

public record PlainAuditFakeCommand : IRequest<string>;

public record AuditableFakeCommand(Guid TargetId) : IRequest<string>, IAuditableCommand
{
    public string AuditAction => "Fake.Done";
    public string AuditTargetType => "Fake";
    public Guid AuditTargetId => TargetId;
}

public record AuditableFakeCommandWithStudio(Guid TargetId, Guid StudioId)
    : IRequest<string>, IAuditableCommand
{
    public string AuditAction => "Fake.WithStudio";
    public string AuditTargetType => "Fake";
    public Guid AuditTargetId => TargetId;
    public Guid? AuditStudioId => StudioId;
}

public class AuditLogBehaviorTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _actorId = Guid.NewGuid();

    public AuditLogBehaviorTests()
    {
        _currentUser.UserId.Returns(_actorId);
        _currentUser.Role.Returns("owner");
        _tenant.IsSet.Returns(false);
    }

    private AuditLogBehavior<TRequest, string> CreateSut<TRequest>() where TRequest : notnull =>
        new(_db, _currentUser, _tenant);

    [Fact]
    public async Task Handle_PlainCommand_DoesNotWriteAuditLog()
    {
        AuditLogBehavior<PlainAuditFakeCommand, string> behavior = CreateSut<PlainAuditFakeCommand>();

        string result = await behavior.Handle(
            new PlainAuditFakeCommand(), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
        _db.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AuditableCommand_WritesAuditLogAfterSuccess()
    {
        Guid targetId = Guid.NewGuid();
        AuditLogBehavior<AuditableFakeCommand, string> behavior = CreateSut<AuditableFakeCommand>();

        await behavior.Handle(
            new AuditableFakeCommand(targetId), _ => Task.FromResult("ok"), CancellationToken.None);

        _db.AuditLogEntries.Should().ContainSingle(e =>
            e.Action == "Fake.Done" &&
            e.TargetType == "Fake" &&
            e.TargetId == targetId &&
            e.ActorUserId == _actorId &&
            e.ActorRole == "owner");
    }

    [Fact]
    public async Task Handle_HandlerThrows_DoesNotWriteAuditLog()
    {
        AuditLogBehavior<AuditableFakeCommand, string> behavior = CreateSut<AuditableFakeCommand>();

        Func<Task> act = () => behavior.Handle(
            new AuditableFakeCommand(Guid.NewGuid()),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExplicitAuditStudioId_UsesItOverTenant()
    {
        Guid studioId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(Guid.NewGuid()); // different from the command's explicit studio

        AuditLogBehavior<AuditableFakeCommandWithStudio, string> behavior =
            CreateSut<AuditableFakeCommandWithStudio>();

        await behavior.Handle(
            new AuditableFakeCommandWithStudio(Guid.NewGuid(), studioId),
            _ => Task.FromResult("ok"), CancellationToken.None);

        _db.AuditLogEntries.Single().StudioId.Should().Be(studioId);
    }

    [Fact]
    public async Task Handle_NoExplicitAuditStudioId_FallsBackToTenantWhenSet()
    {
        Guid tenantStudioId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(tenantStudioId);

        AuditLogBehavior<AuditableFakeCommand, string> behavior = CreateSut<AuditableFakeCommand>();

        await behavior.Handle(
            new AuditableFakeCommand(Guid.NewGuid()), _ => Task.FromResult("ok"), CancellationToken.None);

        _db.AuditLogEntries.Single().StudioId.Should().Be(tenantStudioId);
    }

    [Fact]
    public async Task Handle_NoExplicitAuditStudioId_TenantNotSet_StudioIdIsNull()
    {
        _tenant.IsSet.Returns(false);

        AuditLogBehavior<AuditableFakeCommand, string> behavior = CreateSut<AuditableFakeCommand>();

        await behavior.Handle(
            new AuditableFakeCommand(Guid.NewGuid()), _ => Task.FromResult("ok"), CancellationToken.None);

        _db.AuditLogEntries.Single().StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AuditableCommand_MetadataContainsNoPiiShapedFields()
    {
        AuditLogBehavior<AuditableFakeCommand, string> behavior = CreateSut<AuditableFakeCommand>();

        await behavior.Handle(
            new AuditableFakeCommand(Guid.NewGuid()), _ => Task.FromResult("ok"), CancellationToken.None);

        string metadata = _db.AuditLogEntries.Single().Metadata;
        using JsonDocument doc = JsonDocument.Parse(metadata);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            prop.Name.Should().NotMatchRegex("(?i)email|phone|name|address|note");
        }
    }
}
