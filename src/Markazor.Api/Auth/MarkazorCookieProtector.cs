using System.Buffers.Text;
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
        byte[] payload = new byte[NonceSize + TagSize + plaintext.Length];
        Span<byte> nonce = payload.AsSpan(0, NonceSize);
        Span<byte> tag = payload.AsSpan(NonceSize, TagSize);
        Span<byte> ciphertext = payload.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return Base64Url.EncodeToString(payload);
    }

    public TValue? Unprotect<TValue>(string? protectedValue, JsonTypeInfo<TValue> jsonTypeInfo) where TValue : class
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            ReadOnlySpan<byte> payload = Base64Url.DecodeFromChars(protectedValue);

            if (payload.Length <= NonceSize + TagSize)
            {
                return null;
            }

            ReadOnlySpan<byte> nonce = payload[..NonceSize];
            ReadOnlySpan<byte> tag = payload[NonceSize..(NonceSize + TagSize)];
            ReadOnlySpan<byte> ciphertext = payload[(NonceSize + TagSize)..];
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
