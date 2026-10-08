namespace QVF.Parser.Core.Container;

/// <summary>Adler-32 checksum (RFC 1950), the trailer of every zlib stream.</summary>
internal static class Adler32
{
    private const uint Modulus = 65521;
    // Largest n such that 255n(n+1)/2 + (n+1)(Modulus-1) fits in 32 bits.
    private const int MaxBlock = 5552;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        while (!data.IsEmpty)
        {
            var block = data[..Math.Min(MaxBlock, data.Length)];
            foreach (var value in block)
            {
                a += value;
                b += a;
            }
            a %= Modulus;
            b %= Modulus;
            data = data[block.Length..];
        }
        return (b << 16) | a;
    }
}
