using System.Buffers.Binary;

namespace StudentSync.Application.Content;

/// <summary>
/// Removes privacy-sensitive metadata (EXIF GPS position, camera serials, captions, timestamps) from JPEG and PNG
/// images without decoding or re-encoding them. Only whole metadata segments/chunks are dropped; every byte of the
/// image data itself is copied unchanged, so quality and colour are preserved.
/// <list type="bullet">
/// <item>JPEG: drops APP1 (EXIF/XMP), APP13 (Photoshop/IPTC) and COM segments, and anything after EOI.</item>
/// <item>PNG: drops tEXt, zTXt, iTXt, eXIf and tIME chunks, and anything after IEND.</item>
/// </list>
/// </summary>
public static class ImageMetadataStripper
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly HashSet<string> PngMetadataChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    /// <summary>Returns a copy of <paramref name="image"/> without metadata.</summary>
    /// <exception cref="InvalidImageException">The bytes are not a well-formed image of the given type.</exception>
    public static byte[] Strip(ReadOnlySpan<byte> image, string contentType) => contentType switch
    {
        "image/jpeg" => StripJpeg(image),
        "image/png" => StripPng(image),
        _ => throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType)),
    };

    public static byte[] StripJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) throw new InvalidImageException("Missing JPEG start-of-image marker.");
        using var output = new MemoryStream(data.Length);
        output.Write(data[..2]);
        var pos = 2;
        while (true)
        {
            if (pos >= data.Length) throw new InvalidImageException("JPEG ends before the end-of-image marker.");
            if (data[pos] != 0xFF) throw new InvalidImageException("Unexpected bytes between JPEG segments.");
            // Any number of 0xFF fill bytes may precede a marker.
            while (pos < data.Length && data[pos] == 0xFF) pos++;
            if (pos >= data.Length) throw new InvalidImageException("JPEG ends inside a marker.");
            var marker = data[pos++];

            if (marker == 0xD9) // EOI: done; trailing bytes (often appended metadata) are dropped.
            {
                output.Write([0xFF, 0xD9]);
                return output.ToArray();
            }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) // stand-alone markers without a length
            {
                output.Write([0xFF, marker]);
                continue;
            }
            if (marker == 0x00) throw new InvalidImageException("Invalid JPEG marker.");
            if (pos + 2 > data.Length) throw new InvalidImageException("JPEG segment length is truncated.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
            if (length < 2 || pos + length > data.Length) throw new InvalidImageException("JPEG segment length is invalid.");
            var segment = data.Slice(pos, length);
            pos += length;

            var drop = marker is 0xE1 or 0xED or 0xFE; // APP1 (EXIF/XMP), APP13 (IPTC), COM
            if (!drop)
            {
                output.Write([0xFF, marker]);
                output.Write(segment);
            }

            if (marker == 0xDA)
            {
                // Start of scan: copy the entropy-coded data verbatim up to the next real marker. Inside scan data
                // 0xFF is followed by 0x00 (byte stuffing) or a restart marker, neither of which ends the scan.
                var start = pos;
                while (true)
                {
                    if (pos + 1 >= data.Length) throw new InvalidImageException("JPEG scan data is truncated.");
                    if (data[pos] == 0xFF)
                    {
                        var next = data[pos + 1];
                        if (next == 0x00 || next is >= 0xD0 and <= 0xD7) { pos += 2; continue; }
                        if (next == 0xFF) { pos++; continue; }
                        break;
                    }
                    pos++;
                }
                output.Write(data[start..pos]);
            }
        }
    }

    public static byte[] StripPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngSignature.Length || !data[..PngSignature.Length].SequenceEqual(PngSignature)) throw new InvalidImageException("Missing PNG signature.");
        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);
        var pos = PngSignature.Length;
        var first = true;
        while (true)
        {
            if (pos + 12 > data.Length) throw new InvalidImageException("PNG ends before the IEND chunk.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length) throw new InvalidImageException("PNG chunk length is invalid.");
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            if (first && type != "IHDR") throw new InvalidImageException("PNG must start with an IHDR chunk.");
            first = false;
            var total = 12 + (int)length; // length + type + data + CRC
            if (!PngMetadataChunks.Contains(type)) output.Write(data.Slice(pos, total));
            pos += total;
            if (type == "IEND") return output.ToArray(); // trailing bytes are dropped
        }
    }
}

/// <summary>The uploaded bytes are not a well-formed image.</summary>
public sealed class InvalidImageException(string message) : Exception(message);
