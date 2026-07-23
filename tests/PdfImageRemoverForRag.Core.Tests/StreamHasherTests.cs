using System.Text;
using PdfImageRemoverForRag.Core.Hashing;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24: "SHA-256ハッシュ生成".
// Uses well-known NIST vectors so the test does not depend on library internals.
public class StreamHasherTests
{
    [Fact]
    public void EmptyInput_ReturnsKnownDigest()
    {
        // SHA-256 of the empty string is a documented constant.
        const string expected = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        Assert.Equal(expected, StreamHasher.Sha256Hex(Array.Empty<byte>()));
    }

    [Fact]
    public void KnownVector_abc_ReturnsExpectedDigest()
    {
        // "abc" is the NIST FIPS-180 example vector.
        const string expected = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var bytes = Encoding.ASCII.GetBytes("abc");
        Assert.Equal(expected, StreamHasher.Sha256Hex(bytes));
    }

    [Fact]
    public void SameBytes_ProduceSameHash_DifferentBytes_ProduceDifferentHash()
    {
        // The property that the grouping logic relies on.
        var a = new byte[] { 1, 2, 3, 4, 5 };
        var b = new byte[] { 1, 2, 3, 4, 5 };
        var c = new byte[] { 1, 2, 3, 4, 6 };

        Assert.Equal(StreamHasher.Sha256Hex(a), StreamHasher.Sha256Hex(b));
        Assert.NotEqual(StreamHasher.Sha256Hex(a), StreamHasher.Sha256Hex(c));
    }

    [Fact]
    public void Output_IsUppercaseHexAndCorrectLength()
    {
        // The rest of the code expects uppercase hex (Convert.ToHexString default).
        var hex = StreamHasher.Sha256Hex(Encoding.ASCII.GetBytes("test"));
        Assert.Equal(64, hex.Length);
        Assert.All(hex, c => Assert.True(char.IsDigit(c) || (c >= 'A' && c <= 'F')));
    }
}
