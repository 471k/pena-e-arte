namespace Pena_e_Arte.Infrastructure.Services;

public class VaultOptions
{
    public const string Section = "Vault";

    /// <summary>Vault address, e.g. http://127.0.0.1:8200. Empty = Vault not configured.</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Auth token (dev-mode root token locally; a scoped AppRole/token in production).</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>KV v2 mount point (Vault dev mode enables "secret" by default).</summary>
    public string MountPoint { get; init; } = "secret";
}
