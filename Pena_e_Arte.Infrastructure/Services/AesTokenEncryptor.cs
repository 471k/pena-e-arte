using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// AES-256-GCM authenticated encryption for Instagram access tokens.
/// Key is sourced from Instagram:TokenEncryptionKey (32-byte base64 env var).
/// Output format: base64(nonce[12] + ciphertext + tag[16]).
/// </summary>
public sealed class AesTokenEncryptor(IOptions<InstagramOptions> options) : ITokenEncryptor
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);

    public string Encrypt(string plainText)
    {
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipher = new byte[plainBytes.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        byte[] result = new byte[nonce.Length + cipher.Length + tag.Length];
        nonce.CopyTo(result, 0);
        cipher.CopyTo(result, nonce.Length);
        tag.CopyTo(result, nonce.Length + cipher.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        byte[] raw = Convert.FromBase64String(cipherText);
        int nLen = AesGcm.NonceByteSizes.MaxSize;
        int tagLen = AesGcm.TagByteSizes.MaxSize;
        int cLen = raw.Length - nLen - tagLen;

        byte[] nonce = raw[..nLen];
        byte[] cipher = raw[nLen..(nLen + cLen)];
        byte[] tag = raw[(nLen + cLen)..];
        byte[] plain = new byte[cLen];

        using AesGcm aes = new(_key, tagLen);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
