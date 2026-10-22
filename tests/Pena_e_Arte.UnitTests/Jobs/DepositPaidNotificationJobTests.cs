using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Infrastructure.Jobs;

namespace Pena_e_Arte.UnitTests.Jobs;

public class DepositPaidNotificationJobTests
{
    [Fact]
    public async Task ExecuteAsync_SendsTheDepositReceivedNotificationForThatPayment()
    {
        ISender sender = Substitute.For<ISender>();
        Guid paymentId = Guid.NewGuid();

        await new DepositPaidNotificationJob(sender).ExecuteAsync(paymentId);

        await sender.Received(1).Send(
            Arg.Is<SendDepositCapturedNotificationCommand>(c => c.PaymentId == paymentId),
            Arg.Any<CancellationToken>());
    }
}
