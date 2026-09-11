namespace Pena_e_Arte.Domain.Exceptions;

/// <summary>
/// A studio attempted a card payment operation before connecting a payment provider account
/// (no <see cref="Entities.StudioCredentialRef"/> row / no <see cref="Entities.Studio.PokMerchantId"/>).
/// Distinct from <see cref="ServiceUnavailableException"/> (provider is connected but unreachable
/// or erroring) so the owner-facing UI can tell "go connect your account" apart from "try again
/// later" — see ADR-0001.
/// </summary>
public class PaymentProviderNotConnectedException(string message) : DomainException(message);
