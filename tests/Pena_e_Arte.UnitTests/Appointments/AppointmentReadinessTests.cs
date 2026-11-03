using FluentAssertions;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// DEC03: readiness is derived at read time from facts that already exist. Each requirement is checked
/// satisfied and not; a service with none configured yields an empty set (no checklist), and a cash
/// promise never counts as a paid deposit.
/// </summary>
public class AppointmentReadinessTests
{
    private static ReadinessFacts Facts(
        bool deposit = false, bool intake = false, bool consent = false, bool reference = false,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        DepositStatus depositStatus = DepositStatus.Pending,
        decimal depositAmount = 50m,
        PaymentStatus? payment = null,
        bool consentSigned = false, bool intakeSubmitted = false, bool hasReference = false) =>
        new(deposit, intake, consent, reference, status, depositStatus, depositAmount, payment,
            consentSigned, intakeSubmitted, hasReference);

    [Fact]
    public void NoRequirementsConfigured_IsAnEmptySet_NotZeroes()
    {
        IReadOnlyList<ReadinessItem> items = AppointmentReadiness.Derive(Facts(
            consentSigned: true, intakeSubmitted: true, hasReference: true, depositStatus: DepositStatus.Paid));

        items.Should().BeEmpty();
    }

    [Fact]
    public void AllConfigured_AreListedInPriorityOrder()
    {
        IReadOnlyList<ReadinessItem> items = AppointmentReadiness.Derive(Facts(true, true, true, true));

        items.Select(i => i.Requirement).Should().Equal(
            ReadinessRequirement.DepositPaid,
            ReadinessRequirement.IntakeForm,
            ReadinessRequirement.ConsentForm,
            ReadinessRequirement.ReferenceImage);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void ConsentForm_IsSatisfiedBySigning(bool signed, bool satisfied, bool available)
    {
        ReadinessItem item = AppointmentReadiness.Derive(Facts(consent: true, consentSigned: signed)).Single();

        item.Requirement.Should().Be(ReadinessRequirement.ConsentForm);
        item.Satisfied.Should().Be(satisfied);
        item.Available.Should().Be(available);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void IntakeForm_IsSatisfiedBySubmitting(bool submitted, bool satisfied, bool available)
    {
        ReadinessItem item = AppointmentReadiness.Derive(Facts(intake: true, intakeSubmitted: submitted)).Single();

        item.Requirement.Should().Be(ReadinessRequirement.IntakeForm);
        item.Satisfied.Should().Be(satisfied);
        item.Available.Should().Be(available);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ReferenceImage_IsSatisfiedByAReferenceAttachment_AndIsNeverAnInAppAction(bool hasReference, bool satisfied)
    {
        ReadinessItem item = AppointmentReadiness.Derive(Facts(reference: true, hasReference: hasReference)).Single();

        item.Satisfied.Should().Be(satisfied);
        item.Available.Should().BeFalse("there is no client path to add an attachment after booking");
    }

    [Theory]
    [InlineData(DepositStatus.Paid, null, true)]
    [InlineData(DepositStatus.PrePaid, null, true)]
    [InlineData(DepositStatus.Pending, PaymentStatus.Paid, true)]
    [InlineData(DepositStatus.Pending, PaymentStatus.Captured, true)]
    [InlineData(DepositStatus.Pending, null, false)]
    [InlineData(DepositStatus.Pending, PaymentStatus.Pending, false)]
    [InlineData(DepositStatus.Pending, PaymentStatus.Failed, false)]
    [InlineData(DepositStatus.Refunded, PaymentStatus.Refunded, false)]
    [InlineData(DepositStatus.Forfeited, null, false)]
    public void DepositPaid_UsesTheExistingMeaningOfMoneyReceived(
        DepositStatus depositStatus, PaymentStatus? payment, bool satisfied)
    {
        ReadinessItem item = AppointmentReadiness.Derive(
            Facts(deposit: true, depositStatus: depositStatus, payment: payment)).Single();

        item.Satisfied.Should().Be(satisfied);
    }

    [Fact]
    public void DepositPaid_CashPendingIsAPromise_NotMoneyReceived()
    {
        ReadinessItem item = AppointmentReadiness.Derive(
            Facts(deposit: true, depositStatus: DepositStatus.Pending, payment: PaymentStatus.CashPending)).Single();

        item.Satisfied.Should().BeFalse();
        item.Available.Should().BeFalse("the client already chose cash; there is nothing to do in the app");
    }

    [Fact]
    public void DepositPaid_NothingDue_IsSatisfied()
    {
        ReadinessItem item = AppointmentReadiness.Derive(Facts(deposit: true, depositAmount: 0m)).Single();

        item.Satisfied.Should().BeTrue();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void NoLongerUpcoming_NothingIsAvailable(AppointmentStatus status)
    {
        IReadOnlyList<ReadinessItem> items = AppointmentReadiness.Derive(Facts(true, true, true, true, status: status));

        items.Should().OnlyContain(i => !i.Available);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void Upcoming_OutstandingFormsAndDepositAreAvailable(AppointmentStatus status)
    {
        IReadOnlyList<ReadinessItem> items = AppointmentReadiness.Derive(Facts(true, true, true, false, status: status));

        items.Should().OnlyContain(i => i.Available && !i.Satisfied);
    }
}
