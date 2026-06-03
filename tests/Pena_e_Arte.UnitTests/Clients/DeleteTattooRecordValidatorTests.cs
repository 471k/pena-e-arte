using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class DeleteTattooRecordValidatorTests
{
    private readonly DeleteTattooRecordValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new DeleteTattooRecordCommand(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        _sut.ShouldFailOn(new DeleteTattooRecordCommand(Guid.Empty, Guid.NewGuid()), "ClientId");
    }

    [Fact]
    public void Validate_EmptyId_FailsOnId()
    {
        _sut.ShouldFailOn(new DeleteTattooRecordCommand(Guid.NewGuid(), Guid.Empty), "Id");
    }
}
