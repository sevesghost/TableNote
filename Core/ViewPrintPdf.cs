using System.Globalization;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TableNote;

/// <summary>
/// One multi-page PDF so the Windows shell print verb can send every page as a single job.
/// Each page is a lossless RGB image. The preview still shows the PNGs.
/// </summary>
public static class ViewPrintPdf
{
    public static byte[] FromPngs(IReadOnlyList<byte[]> pngPages, double dpi)
    {
        ArgumentNullException.ThrowIfNull(pngPages);
        if (pngPages.Count == 0) throw new ArgumentException("No pages to print.", nameof(pngPages));
        if (dpi < 1) dpi = ViewPrintSpec.DefaultDpi;
        var images = new List<PdfImage>(pngPages.Count);
        foreach (var png in pngPages)
        {
            using var stream = new MemoryStream(png, writable: false);
            using var bitmap = new Bitmap(stream);
            images.Add(EncodePage(bitmap));
        }
        return Compose(images, dpi);
    }

    private static PdfImage EncodePage(Bitmap bitmap)
    {
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        try
        {
            var rgb = ToRgb(bitmap);
            return new PdfImage(Zlib(rgb), w, h, false);
        }
        catch
        {
            using var jpeg = new MemoryStream();
            bitmap.Save(jpeg, new JpegBitmapEncoderOptions { Quality = 92 });
            return new PdfImage(jpeg.ToArray(), w, h, true);
        }
    }

    private static byte[] ToRgb(Bitmap bitmap)
    {
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        if (w < 1 || h < 1) throw new InvalidOperationException("empty page");
        var format = bitmap.Format;
        if (format == PixelFormat.Bgra8888) return Pack(bitmap, w, h, bgra: true);
        if (format == PixelFormat.Rgba8888) return Pack(bitmap, w, h, bgra: false);
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var frame = copy.Lock())
            bitmap.CopyPixels(frame);
        return Pack(copy, w, h, bgra: false);
    }

    private static byte[] Pack(Bitmap bitmap, int w, int h, bool bgra)
    {
        var raw = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = raw)
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, raw.Length, w * 4);
        }
        var rgb = new byte[w * h * 3];
        for (int i = 0, o = 0; i < raw.Length; i += 4, o += 3)
        {
            rgb[o] = bgra ? raw[i + 2] : raw[i];
            rgb[o + 1] = raw[i + 1];
            rgb[o + 2] = bgra ? raw[i] : raw[i + 2];
        }
        return rgb;
    }

    /// <summary>zlib wrapper (RFC 1950) around a raw deflate stream. PDF FlateDecode requires that wrapper.</summary>
    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream(data.Length / 8 + 64);
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        var sum = Adler32(data);
        ms.WriteByte((byte)(sum >> 24));
        ms.WriteByte((byte)(sum >> 16));
        ms.WriteByte((byte)(sum >> 8));
        ms.WriteByte((byte)sum);
        return ms.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        for (var i = 0; i < data.Length; i++)
        {
            a += data[i];
            b += a;
            if ((i & 4095) != 4095) continue;
            a %= mod;
            b %= mod;
        }
        return ((b % mod) << 16) | (a % mod);
    }

    private static byte[] Compose(List<PdfImage> images, double dpi)
    {
        using var ms = new MemoryStream();
        void Text(string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            ms.Write(bytes, 0, bytes.Length);
        }

        Text("%PDF-1.4\n");
        var offsets = new List<long>(2 + images.Count * 3);
        void Obj(int id, Action write)
        {
            offsets.Add(ms.Position);
            Text(id.ToString(CultureInfo.InvariantCulture));
            Text(" 0 obj\n");
            write();
            Text("\nendobj\n");
        }

        var n = images.Count;
        Obj(1, () => Text("<< /Type /Catalog /Pages 2 0 R >>"));
        var kids = string.Join(' ', Enumerable.Range(0, n).Select(i => (3 + i * 3).ToString(CultureInfo.InvariantCulture) + " 0 R"));
        Obj(2, () => Text("<< /Type /Pages /Count " + n.ToString(CultureInfo.InvariantCulture) + " /Kids [" + kids + "] >>"));
        for (var i = 0; i < n; i++)
        {
            var image = images[i];
            var pageId = 3 + i * 3;
            var contentId = pageId + 1;
            var imageId = pageId + 2;
            var pageW = (image.Width / dpi * 72.0).ToString("0.###", CultureInfo.InvariantCulture);
            var pageH = (image.Height / dpi * 72.0).ToString("0.###", CultureInfo.InvariantCulture);
            Obj(pageId, () => Text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pageW + " " + pageH
                + "] /Contents " + contentId.ToString(CultureInfo.InvariantCulture)
                + " 0 R /Resources << /XObject << /Im0 " + imageId.ToString(CultureInfo.InvariantCulture) + " 0 R >> >> >>"));
            // Bitmap rows are top to bottom. PDF user space grows upward, so the image is flipped
            // onto the page (first row at the top). One document, one Windows print verb.
            var content = Encoding.ASCII.GetBytes("q\n" + pageW + " 0 0 -" + pageH + " 0 " + pageH + " cm\n/Im0 Do\nQ\n");
            Obj(contentId, () =>
            {
                Text("<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
                ms.Write(content, 0, content.Length);
                Text("\nendstream");
            });
            var filter = image.Jpeg ? "/DCTDecode" : "/FlateDecode";
            Obj(imageId, () =>
            {
                Text("<< /Type /XObject /Subtype /Image /Width " + image.Width.ToString(CultureInfo.InvariantCulture)
                    + " /Height " + image.Height.ToString(CultureInfo.InvariantCulture)
                    + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter " + filter
                    + " /Length " + image.Data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
                ms.Write(image.Data, 0, image.Data.Length);
                Text("\nendstream");
            });
        }

        var xref = ms.Position;
        var count = offsets.Count;
        Text("xref\n0 " + (count + 1).ToString(CultureInfo.InvariantCulture) + "\n");
        Text("0000000000 65535 f \n");
        foreach (var offset in offsets)
            Text(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text("trailer\n<< /Size " + (count + 1).ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\n");
        Text("startxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return ms.ToArray();
    }

    private readonly record struct PdfImage(byte[] Data, int Width, int Height, bool Jpeg);
}
