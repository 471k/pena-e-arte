namespace Pena_e_Arte.Domain.Interfaces;

public interface ITokenEncryptor
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
