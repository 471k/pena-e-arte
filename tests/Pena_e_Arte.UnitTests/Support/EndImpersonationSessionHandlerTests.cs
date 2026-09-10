using FluentAssertions;
using Pena_e_Arte.Application.Support.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Support;

public class EndImpersonationSessionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private EndImpersonationSessionHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ActiveSession_SetsEndedAt()
    {
        Guid sessionId = await SeedSessionAsync();

        await CreateSut().Handle(new EndImpersonationSessionCommand(sessionId), default);

        ImpersonationSession stored = _db.ImpersonationSessions.Single(s => s.Id == sessionId);
        stored.EndedAt.Should().NotBeNull();
        stored.EndedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_AlreadyEndedSession_DoesNotOverwriteOriginalEndedAt()
    {
        Guid sessionId = await SeedSessionAsync();
        await CreateSut().Handle(new EndImpersonationSessionCommand(sessionId), default);
        DateTime firstEndedAt = _db.ImpersonationSessions.Single(s => s.Id == sessionId).EndedAt!.Value;
        _db.ChangeTracker.Clear();

        await Task.Delay(50);
        await CreateSut().Handle(new EndImpersonationSessionCommand(sessionId), default);

        _db.ImpersonationSessions.Single(s => s.Id == sessionId).EndedAt.Should().Be(firstEndedAt);
    }

    [Fact]
    public async Task Handle_UnknownSessionId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new EndImpersonationSessionCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedSessionAsync()
    {
        ImpersonationSession session = ImpersonationSession.Start(
            Guid.NewGuid(), Guid.NewGuid(), "Investigating a support ticket", TimeSpan.FromMinutes(45));
        _db.ImpersonationSessions.Add(session);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return session.Id;
    }
}

public class EndImpersonationSessionValidatorTests
{
    private readonly EndImpersonationSessionValidator _validator = new();

    [Fact]
    public void Validate_EmptySessionId_Fails() =>
        _validator.Validate(new EndImpersonationSessionCommand(Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ValidSessionId_Succeeds() =>
        _validator.Validate(new EndImpersonationSessionCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
}
