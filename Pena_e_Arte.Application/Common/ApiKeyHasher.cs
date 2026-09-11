using System.Security.Cryptography;
using System.Text;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Generates and hashes external-API secrets. The raw secret is high-entropy (256 bits)
/// and shown to the owner exactly once at creation — SHA-256 of the full string is enough
/// to defeat brute-forcing without needing a slow/salted password-style hash.
/// </summary>
public static class ApiKeyHasher
{
    private const string Prefix = "tos_live_";

    public static (string RawKey, string KeyPrefix, string KeyHash) GenerateNew()
    {
        byte[] secretBytes = RandomNumberGenerator.GetBytes(32);
        string rawKey = Prefix + Convert.ToHexStringLower(secretBytes);
        return (rawKey, KeyPrefix(rawKey), Hash(rawKey));
    }

    public static string Hash(string rawKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

    private static string KeyPrefix(string rawKey) => rawKey[..(Prefix.Length + 4)];
}
