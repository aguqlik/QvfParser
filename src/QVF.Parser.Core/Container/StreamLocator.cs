using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace QVF.Parser.Core.Container;

/// <summary>
/// Finds and decompresses the zlib streams embedded in a QVF.
/// </summary>
/// <remarks>
/// QVF stores no stream index we rely on, so the file is scanned for zlib
/// headers. <see cref="ZLibStream"/> does not report how many input bytes it
/// consumed (it reads ahead in chunks), so the exact end of a stream is found
/// by locating its big-endian Adler-32 trailer in the bytes it read. The same
/// check rejects false-positive headers.
/// </remarks>
internal static class StreamLocator
{
    private const byte ZlibCmf = 0x78;
    private const int TrailerSize = 4;

    public static List<QvfPayload> Locate(byte[] file)
    {
        var payloads = new List<QvfPayload>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        while (position < file.Length - 1)
        {
            var found = file.AsSpan(position).IndexOf(ZlibCmf);
            if (found < 0)
                break;
            var offset = position + found;
            if (!IsZlibHeader(file, offset) || !TryInflate(file, offset, out var data, out var consumed))
            {
                position = offset + 1;
                continue;
            }

            position = offset + consumed;
            if (data.Length == 0)
                continue;
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
            if (seen.Add(sha256))
                payloads.Add(new QvfPayload(payloads.Count + 1, offset, consumed, data, sha256));
        }
        return payloads;
    }

    private static bool IsZlibHeader(byte[] file, int offset) =>
        offset + 1 < file.Length && file[offset + 1] is 0x01 or 0x5E or 0x9C or 0xDA;

    private static bool TryInflate(byte[] file, int offset, out byte[] data, out int consumed)
    {
        data = [];
        consumed = 0;
        using var input = new MemoryStream(file, offset, file.Length - offset, writable: false);
        using var output = new MemoryStream();
        try
        {
            using var inflater = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
            inflater.CopyTo(output);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        data = output.ToArray();
        var read = (int)input.Position;
        Span<byte> trailer = stackalloc byte[TrailerSize];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, Adler32.Compute(data));
        // Smallest possible zlib stream: 2-byte header, 2-byte empty deflate block, trailer.
        var window = file.AsSpan(offset + 2, Math.Max(0, read - 2));
        var at = window.IndexOf(trailer);
        if (at < 0)
            return false; // truncated, corrupt, or not a real stream
        consumed = 2 + at + TrailerSize;
        return true;
    }
}
