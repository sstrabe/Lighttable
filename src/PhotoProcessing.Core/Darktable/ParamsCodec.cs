using System.IO.Compression;

namespace PhotoProcessing.Core.Darktable;

/// <summary>
/// Encodes/decodes the <c>darktable:params</c> / <c>darktable:blendop_params</c> attribute values.
/// darktable writes either plain lowercase hex of the raw C struct, or
/// <c>gz</c> + two-digit compression factor + base64(zlib(struct)) for larger blobs.
/// See <c>dt_exif_xmp_decode</c> / <c>dt_exif_xmp_encode</c> in src/common/exif.cc.
/// </summary>
public static class ParamsCodec
{
    public static byte[] Decode(string value)
    {
        if (value.StartsWith("gz", StringComparison.Ordinal))
        {
            var compressed = Convert.FromBase64String(value[4..]);
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        return Convert.FromHexString(value);
    }

    /// <summary>Always writes plain hex; darktable reads it regardless of size.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);
}
