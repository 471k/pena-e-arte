using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Application.Reminders.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reminders;

public class CreateManualReminderValidatorTests
{
    private readonly CreateManualReminderValidator _sut = new();

    [Fact]
    public void Validate_ValidRawContactCommand_IsValid()
    {
        _sut.ShouldBeValid(RawContactCommand(phone: "+351912345678"));
    }

    [Fact]
    public void Validate_ValidE164Phone_IsValid()
    {
        _sut.ShouldBeValid(RawContactCommand(phone: "+351912345678"));
    }

    [Fact]
    public void Validate_NationalFormatPhoneWithNoPlus_FailsOnRecipientPhone()
    {
        _sut.ShouldFailOn(RawContactCommand(phone: "912345678"), "Request.RecipientPhone");
    }

    [Fact]
    public void Validate_NonPhoneShapedRecipientPhone_FailsOnRecipientPhone()
    {
        _sut.ShouldFailOn(RawContactCommand(phone: "not a phone"), "Request.RecipientPhone");
    }

    [Fact]
    public void Validate_EmptyRecipientPhoneOnRawContactPath_FailsWithNotEmptyNotFormatError()
    {
        ValidationResult result = _sut.Validate(RawContactCommand(phone: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Request.RecipientPhone");
    }

    private static CreateManualReminderCommand RawContactCommand(string phone) =>
        new(new CreateManualReminderRequest(
            AppointmentId: null,
            ClientId: null,
            ArtistId: null,
            RecipientName: "Wendy",
            RecipientPhone: phone,
            Message: null,
            ScheduledFor: null));
}
