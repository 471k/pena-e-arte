using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Services;
using Stripe;

namespace Pena_e_Arte.UnitTests.Services;

public class StripePaymentServiceTests
{
    private readonly PaymentIntentService _intentService = Substitute.For<PaymentIntentService>();
    private readonly RefundService        _refundService = Substitute.For<RefundService>();

    private StripePaymentService CreateSut() =>
        new(_intentService, _refundService);

    [Fact]
    public async Task CreatePaymentIntentAsync_ReturnsIntentIdAndClientSecret()
    {
        PaymentIntent fake = new() { Id = "pi_fake_123", ClientSecret = "pi_fake_123_secret" };
        _intentService.CreateAsync(Arg.Any<PaymentIntentCreateOptions>(), null, Arg.Any<CancellationToken>())
            .Returns(fake);

        (string id, string secret) = await CreateSut()
            .CreatePaymentIntentAsync(5000L, "eur", Guid.NewGuid(), default);

        id.Should().Be("pi_fake_123");
        secret.Should().Be("pi_fake_123_secret");
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_SetsManualCaptureMethod()
    {
        PaymentIntent fake = new() { Id = "pi_x", ClientSecret = "cs_x" };
        PaymentIntentCreateOptions? captured = null;
        _intentService.CreateAsync(Arg.Do<PaymentIntentCreateOptions>(o => captured = o), null, Arg.Any<CancellationToken>())
            .Returns(fake);

        await CreateSut().CreatePaymentIntentAsync(5000L, "eur", Guid.NewGuid(), default);

        captured!.CaptureMethod.Should().Be("manual");
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_DoesNotPassStripeAccountRequestOptions()
    {
        PaymentIntent fake = new() { Id = "pi_x", ClientSecret = "cs_x" };
        RequestOptions? capturedOpts = null;
        _intentService.CreateAsync(
                Arg.Any<PaymentIntentCreateOptions>(),
                Arg.Do<RequestOptions?>(o => capturedOpts = o),
                Arg.Any<CancellationToken>())
            .Returns(fake);

        await CreateSut().CreatePaymentIntentAsync(5000L, "eur", Guid.NewGuid(), default);

        // Aggregator model: no connected account header sent
        capturedOpts.Should().BeNull();
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_StoresPaymentIdInMetadata()
    {
        Guid paymentId = Guid.NewGuid();
        PaymentIntent fake = new() { Id = "pi_x", ClientSecret = "cs_x" };
        PaymentIntentCreateOptions? captured = null;
        _intentService.CreateAsync(Arg.Do<PaymentIntentCreateOptions>(o => captured = o), null, Arg.Any<CancellationToken>())
            .Returns(fake);

        await CreateSut().CreatePaymentIntentAsync(5000L, "eur", paymentId, default);

        captured!.Metadata.Should().ContainKey("payment_id")
            .WhoseValue.Should().Be(paymentId.ToString());
    }

    [Fact]
    public async Task CapturePaymentAsync_CallsIntentCaptureWithCorrectId()
    {
        _intentService.CaptureAsync("pi_to_capture", null, null, Arg.Any<CancellationToken>())
            .Returns(new PaymentIntent { Id = "pi_to_capture" });

        await CreateSut().CapturePaymentAsync("pi_to_capture", default);

        await _intentService.Received(1)
            .CaptureAsync("pi_to_capture", null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundPaymentIntentAsync_ReturnsRefundId()
    {
        _refundService.CreateAsync(Arg.Any<RefundCreateOptions>(), null, Arg.Any<CancellationToken>())
            .Returns(new Refund { Id = "re_fake_456" });

        string refundId = await CreateSut()
            .RefundPaymentIntentAsync("pi_x", null, default);

        refundId.Should().Be("re_fake_456");
    }

    [Fact]
    public async Task RefundPaymentIntentAsync_PassesAmountToStripe()
    {
        RefundCreateOptions? captured = null;
        _refundService.CreateAsync(
                Arg.Do<RefundCreateOptions>(o => captured = o),
                null, Arg.Any<CancellationToken>())
            .Returns(new Refund { Id = "re_x" });

        await CreateSut().RefundPaymentIntentAsync("pi_y", 2500L, default);

        captured!.Amount.Should().Be(2500L);
        captured.PaymentIntent.Should().Be("pi_y");
    }

    [Fact]
    public async Task RefundPaymentIntentAsync_NullAmount_PassesNullToStripe()
    {
        RefundCreateOptions? captured = null;
        _refundService.CreateAsync(
                Arg.Do<RefundCreateOptions>(o => captured = o),
                null, Arg.Any<CancellationToken>())
            .Returns(new Refund { Id = "re_x" });

        await CreateSut().RefundPaymentIntentAsync("pi_y", null, default);

        captured!.Amount.Should().BeNull();
    }
}
