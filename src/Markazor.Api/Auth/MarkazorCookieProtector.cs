using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Markazor.Api.Auth;

public sealed class MarkazorCookieProtector(string secret)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public string Protect<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];

        using AesGcm aes = new(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return Base64Url.Encode(payload);
    }

    public TValue? Unprotect<TValue>(string? protectedValue, JsonTypeInfo<TValue> jsonTypeInfo) where TValue : class
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            byte[] payload = Base64Url.Decode(protectedValue);

            if (payload.Length <= NonceSize + TagSize)
            {
                return null;
            }

            byte[] nonce = payload[..NonceSize];
            byte[] tag = payload[NonceSize..(NonceSize + TagSize)];
            byte[] ciphertext = payload[(NonceSize + TagSize)..];
            byte[] plaintext = new byte[ciphertext.Length];

            using AesGcm aes = new(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return JsonSerializer.Deserialize(plaintext, jsonTypeInfo);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
