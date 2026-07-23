using System.Security.Cryptography;

namespace PdfImageRemoverForRag.Core.Hashing;

/// <summary>
/// Deterministic SHA-256 helper used by Infrastructure when it builds
/// <c>ImageDiscovery</c> records. Extracted into Core so tests can exercise
/// the exact hex formatting used elsewhere in the app (§10, §24).
/// </summary>
public static class StreamHasher
{
    /// <summary>
    /// Hash a byte span and return uppercase hex — matches
    /// <c>Convert.ToHexString</c> so results are directly comparable in tests.
    /// </summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        // Hash-in-one-shot avoids allocating an IncrementalHash for what is
        // usually a single-buffer input.
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }

    /// <summary>Convenience overload for arrays.</summary>
    public static string Sha256Hex(byte[] data) => Sha256Hex(data.AsSpan());
}
