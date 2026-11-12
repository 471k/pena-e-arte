using FluentAssertions;
using Pena_e_Arte.Application.Payments.Queries;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentsValidatorTests
{
    private readonly GetPaymentsValidator _sut = new();

    [Fact]
    public void Defaults_AreValid() => _sut.Validate(new GetPaymentsQuery()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Pending")]
    [InlineData("CashPending")]
    [InlineData("Captured")]
    [InlineData("Paid")]
    [InlineData("Refunded")]
    [InlineData("Failed")]
    public void EveryStatusName_IsAccepted(string status) =>
        _sut.Validate(new GetPaymentsQuery(Status: status)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("paid")]       // case matters, as for the pay-model arrangement names
    [InlineData("1")]          // Enum.TryParse alone would take the number
    [InlineData(" Paid")]
    [InlineData("Settled")]
    [InlineData("")]
    public void AnythingButAnExactStatusName_IsRefused_NotTreatedAsAll(string status) =>
        _sut.Validate(new GetPaymentsQuery(Status: status)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSize_OutsideOneToHundred_IsRefused(int size) =>
        _sut.Validate(new GetPaymentsQuery(PageSize: size)).IsValid.Should().BeFalse();

    [Fact]
    public void PageSize_Hundred_IsAccepted() =>
        _sut.Validate(new GetPaymentsQuery(PageSize: 100)).IsValid.Should().BeTrue();

    [Fact]
    public void ToBeforeFrom_IsRefused() =>
        _sut.Validate(new GetPaymentsQuery(From: new DateOnly(2026, 10, 31), To: new DateOnly(2026, 10, 1)))
            .IsValid.Should().BeFalse();

    [Fact]
    public void SameDay_IsAccepted() =>
        _sut.Validate(new GetPaymentsQuery(From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 1)))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Search_LongerThanTheCap_IsRefused() =>
        _sut.Validate(new GetPaymentsQuery(Search: new string('a', 101))).IsValid.Should().BeFalse();
}
