using System.Security.Cryptography;

namespace Markazor.Api.Auth;

internal static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        int padding = padded.Length % 4;

        if (padding > 0)
        {
            padded = padded.PadRight(padded.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(padded);
    }

    public static string RandomToken(int byteCount = 32)
    {
        return Encode(RandomNumberGenerator.GetBytes(byteCount));
    }
}
