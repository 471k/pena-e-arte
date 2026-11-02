using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// POK refused an order because its amount is above POK's per-payment maximum (400 "The maximum amount of
/// ... per transaction was exceeded"). Thrown by <see cref="PokAuthClient.SendAsync{T}"/>; order creation
/// (<see cref="PokPaymentProvider.CreatePaymentHoldAsync"/>) turns it into a message the client can act on.
/// Derives from <see cref="ServiceUnavailableException"/> with the old text, so any other caller that does not
/// catch it behaves exactly as before (503 "POK request failed (400).").
/// </summary>
public sealed class PokAmountLimitExceededException() : ServiceUnavailableException("POK request failed (400).");
