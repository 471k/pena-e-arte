using FluentAssertions;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class DeleteDesignRevisionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private DeleteDesignRevisionHandler CreateSut() => new(_db);

    private async Task<(Design design, DesignRevision revision)> Seed()
    {
        Design design = new()
        {
            ClientId    = Guid.NewGuid(),
            ArtistId    = Guid.NewGuid(),
            Title       = "Dragon sleeve",
        };
        _db.Designs.Add(design);

        DesignRevision revision = new()
        {
            DesignId      = design.Id,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/rev1.png",
        };
        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return (design, revision);
    }

    [Fact]
    public async Task Handle_ExistingRevision_RemovesFromDatabase()
    {
        (Design design, DesignRevision revision) = await Seed();

        await CreateSut().Handle(
            new DeleteDesignRevisionCommand(design.Id, revision.Id), default);

        DesignRevision? deleted = await _db.DesignRevisions.FindAsync(revision.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RevisionNotFound_ThrowsNotFoundException()
    {
        (Design design, _) = await Seed();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteDesignRevisionCommand(design.Id, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WrongDesignId_ThrowsNotFoundException()
    {
        (_, DesignRevision revision) = await Seed();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteDesignRevisionCommand(Guid.NewGuid(), revision.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
