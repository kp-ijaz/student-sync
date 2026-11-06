using System.Buffers.Binary;
using System.Text;
using StudentSync.Application.Content;

namespace StudentSync.UnitTests;

public sealed class ImageMetadataStripperTests
{
    // ---------- JPEG ----------

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var s = new byte[4 + payload.Length];
        s[0] = 0xFF;
        s[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(s, 4);
        return s;
    }

    private static readonly byte[] Soi = [0xFF, 0xD8];
    private static readonly byte[] Eoi = [0xFF, 0xD9];
    private static readonly byte[] App0 = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] Dqt = Segment(0xDB, [0, .. Enumerable.Range(1, 64).Select(i => (byte)i)]);
    private static readonly byte[] Sof0 = Segment(0xC0, [8, 0, 16, 0, 16, 1, 1, 0x11, 0]);
    private static readonly byte[] Dht = Segment(0xC4, [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5]);
    private static readonly byte[] Sos = Segment(0xDA, [1, 1, 0, 0, 63, 0]);
    // Entropy-coded data with a stuffed 0xFF00 and a restart marker, which must not be mistaken for segment boundaries.
    private static readonly byte[] ScanData = [0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78, 0x9A];
    private static readonly byte[] Exif = Segment(0xE1, [.. "Exif\0\0"u8, .. "GPSLatitude=25.2048;GPSLongitude=55.2708"u8]);
    private static readonly byte[] Xmp = Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta/>"u8]);
    private static readonly byte[] Iptc = Segment(0xED, [.. "Photoshop 3.0\0"u8, 1, 2, 3]);
    private static readonly byte[] Comment = Segment(0xFE, "Taken at home"u8.ToArray());

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Jpeg_metadata_segments_are_removed_and_image_segments_are_kept_byte_for_byte()
    {
        var input = Concat(Soi, App0, Exif, Xmp, Iptc, Comment, Dqt, Sof0, Dht, Sos, ScanData, Eoi);
        var output = ImageMetadataStripper.Strip(input, "image/jpeg");

        Assert.Equal(Concat(Soi, App0, Dqt, Sof0, Dht, Sos, ScanData, Eoi), output);
        var text = Encoding.Latin1.GetString(output);
        Assert.DoesNotContain("GPS", text);
        Assert.DoesNotContain("Exif", text);
        Assert.DoesNotContain("Taken at home", text);
        Assert.Contains("JFIF", text);
    }

    [Fact]
    public void Jpeg_without_metadata_is_unchanged_and_trailing_bytes_after_eoi_are_dropped()
    {
        var clean = Concat(Soi, App0, Dqt, Sof0, Dht, Sos, ScanData, Eoi);
        Assert.Equal(clean, ImageMetadataStripper.Strip(clean, "image/jpeg"));
        Assert.Equal(clean, ImageMetadataStripper.Strip(Concat(clean, "hidden trailer"u8.ToArray()), "image/jpeg"));
    }

    [Fact]
    public void Progressive_jpeg_with_several_scans_keeps_every_scan()
    {
        var input = Concat(Soi, Exif, Dqt, Sof0, Dht, Sos, ScanData, Dht, Comment, Sos, ScanData, Eoi);
        Assert.Equal(Concat(Soi, Dqt, Sof0, Dht, Sos, ScanData, Dht, Sos, ScanData, Eoi), ImageMetadataStripper.Strip(input, "image/jpeg"));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]          // not a JPEG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0x7F, 0xFF })] // segment length past the end
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02 })] // no end-of-image
    public void Malformed_jpeg_is_rejected(byte[] data) =>
        Assert.Throws<InvalidImageException>(() => ImageMetadataStripper.Strip(data, "image/jpeg"));

    // ---------- PNG ----------

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Chunk(string type, byte[] data)
    {
        var c = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(c, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
        data.CopyTo(c, 8);
        BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(8 + data.Length), 0xDEADBEEF); // CRC is copied, not recomputed
        return c;
    }

    private static readonly byte[] Ihdr = Chunk("IHDR", [0, 0, 0, 16, 0, 0, 0, 16, 8, 2, 0, 0, 0]);
    private static readonly byte[] Plte = Chunk("PLTE", [255, 0, 0]);
    private static readonly byte[] Idat = Chunk("IDAT", [0x78, 0x9C, 1, 2, 3, 4]);
    private static readonly byte[] Iend = Chunk("IEND", []);

    [Fact]
    public void Png_text_exif_and_time_chunks_are_removed_and_image_chunks_are_kept()
    {
        var input = Concat(PngSignature, Ihdr, Chunk("tEXt", "Comment\0GPS 25.2,55.3"u8.ToArray()), Chunk("eXIf", "MM\0*GPS"u8.ToArray()),
            Chunk("iTXt", "XML:com.adobe.xmp\0\0\0\0\0<x/>"u8.ToArray()), Chunk("zTXt", "Raw\0\0xyz"u8.ToArray()), Chunk("tIME", [7, 234, 1, 1, 0, 0, 0]),
            Plte, Idat, Idat, Iend);
        var output = ImageMetadataStripper.Strip(input, "image/png");

        Assert.Equal(Concat(PngSignature, Ihdr, Plte, Idat, Idat, Iend), output);
        var text = Encoding.Latin1.GetString(output);
        Assert.DoesNotContain("GPS", text);
        Assert.DoesNotContain("tEXt", text);
        Assert.DoesNotContain("eXIf", text);
    }

    [Fact]
    public void Png_trailing_bytes_after_iend_are_dropped()
    {
        var clean = Concat(PngSignature, Ihdr, Idat, Iend);
        Assert.Equal(clean, ImageMetadataStripper.Strip(Concat(clean, "appended"u8.ToArray()), "image/png"));
    }

    [Fact]
    public void Malformed_png_is_rejected()
    {
        Assert.Throws<InvalidImageException>(() => ImageMetadataStripper.Strip("not a png"u8.ToArray(), "image/png"));
        Assert.Throws<InvalidImageException>(() => ImageMetadataStripper.Strip(Concat(PngSignature, Idat, Iend), "image/png")); // IHDR must be first
        Assert.Throws<InvalidImageException>(() => ImageMetadataStripper.Strip(Concat(PngSignature, Ihdr, Idat), "image/png"));  // no IEND
        Assert.Throws<InvalidImageException>(() => ImageMetadataStripper.Strip(Concat(PngSignature, Ihdr, Idat[..10]), "image/png")); // truncated chunk
    }
}
