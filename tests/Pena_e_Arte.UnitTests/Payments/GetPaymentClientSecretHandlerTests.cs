using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentClientSecretHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly ICurrentUser  _user     = Substitute.For<ICurrentUser>();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetPaymentClientSecretHandler CreateSut() => new(_db, _user);

    [Fact]
    public async Task Handle_OwnerRole_ReturnsSecret()
    {
        Guid paymentId = await SeedPayment("pi_test_secret_abc");
        _user.Role.Returns("owner");

        PaymentClientSecretResponse result = await CreateSut()
            .Handle(new GetPaymentClientSecretQuery(paymentId), default);

        result.ClientSecret.Should().Be("pi_test_secret_abc");
    }

    [Fact]
    public async Task Handle_ClientRole_MatchingClient_ReturnsSecret()
    {
        Guid userId   = Guid.NewGuid();
        Guid clientId = Guid.NewGuid();
        _db.Clients.Add(new Client
        {
            Id       = clientId,
            StudioId = _studioId,
            UserId   = userId,
            Email    = "client@test.com"
        });
        await _db.SaveChangesAsync();

        Guid paymentId = await SeedPaymentForClient("pi_secret_client", clientId);
        _user.Role.Returns("client");
        _user.UserId.Returns(userId);

        PaymentClientSecretResponse result = await CreateSut()
            .Handle(new GetPaymentClientSecretQuery(paymentId), default);

        result.ClientSecret.Should().Be("pi_secret_client");
    }

    [Fact]
    public async Task Handle_ClientRole_DifferentClient_ThrowsUnauthorized()
    {
        Guid otherClientId = Guid.NewGuid();
        Guid paymentId     = await SeedPayment("pi_secret_other");
        _user.Role.Returns("client");
        _user.UserId.Returns(Guid.NewGuid()); // not linked to any client

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientSecretQuery(paymentId), default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_UnknownPayment_ThrowsNotFound()
    {
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientSecretQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MissingClientSecret_ThrowsNotFound()
    {
        Guid paymentId = await SeedPayment(null);
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientSecretQuery(paymentId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedPayment(string? secret)
    {
        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId      = Guid.NewGuid(),
            Amount        = 50m,
            Status        = PaymentStatus.Pending,
            ClientSecret  = secret
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedPaymentForClient(string secret, Guid clientId)
    {
        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId      = clientId,
            Amount        = 50m,
            Status        = PaymentStatus.Pending,
            ClientSecret  = secret
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
