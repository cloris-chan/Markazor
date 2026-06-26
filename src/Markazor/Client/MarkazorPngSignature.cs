namespace Markazor.Client;

internal static class MarkazorPngSignature
{
    public static bool Matches(ReadOnlyMemory<byte> content)
    {
        ReadOnlySpan<byte> bytes = content.Span;

        return bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A;
    }
}
