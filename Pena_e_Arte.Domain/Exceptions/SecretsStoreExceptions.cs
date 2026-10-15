namespace Pena_e_Arte.Domain.Exceptions;

/// <summary>
/// The secrets backend answered, and the secret (or the requested field) isn't there — e.g. a
/// studio whose POK keys were never written. Callers treat this as "not set up": ask the owner to
/// reconnect. Thrown by <see cref="Interfaces.ISecretsProvider"/> implementations.
/// </summary>
public class SecretNotFoundException(string message) : InvalidOperationException(message);

/// <summary>
/// The secrets backend couldn't be used at all — sealed, unreachable, timed out or returning an
/// error (go-live blocker C-8). Callers must treat this as a temporary outage (503 + an ops alert),
/// never as "reconnect": the stored credentials are still there once the backend recovers.
/// </summary>
public class SecretsStoreUnavailableException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
