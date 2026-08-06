using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// <see cref="IImageResampler"/> on GDI+, which is the imaging library this
/// application already has. Lives in the App layer for the reason
/// <see cref="WindowsPageRasterizer"/> does: System.Drawing is Windows-only and
/// the layer that rewrites PDFs is not.
/// </summary>
internal sealed class WindowsImageResampler : IImageResampler
{
    public StoredImage? Resize(StoredImage image, int width, int height, int jpegQuality)
    {
        if (width < 1 || height < 1) return null;

        try
        {
            using var source = Decode(image);
            if (source is null) return null;

            using var resized = RedrawAt(source, width, height);
            var mask = ResizeMask(image.SoftMask, width, height);

            if (image.Encoding == StoredImageEncoding.Jpeg)
            {
                var encoded = EncodeJpeg(resized, jpegQuality);
                return encoded is null
                    ? null
                    : image with { Width = width, Height = height, Data = encoded, SoftMask = mask };
            }

            return image with
            {
                Width = width,
                Height = height,
                Data = ExtractSamples(resized, image.Components),
                SoftMask = mask,
            };
        }
        catch (Exception)
        {
            // An image that will not decode has to leave the caller able to keep
            // it as it was, which is what a null says.
            return null;
        }
    }

    /// <summary>The image as a bitmap, whichever way its bytes are stored.</summary>
    static Bitmap? Decode(StoredImage image)
    {
        if (image.Encoding == StoredImageEncoding.Jpeg)
        {
            using var stream = new MemoryStream(image.Data);
            using var decoded = Image.FromStream(stream);
            return new Bitmap(decoded);
        }

        return FromSamples(image.Data, image.Width, image.Height, image.Components);
    }

    /// <summary>
    /// A bitmap built from raw samples. PDF stores them tightly packed, row by
    /// row, while GDI+ wants a stride it chooses and colour components the other
    /// way round, so the rows are copied one at a time rather than in one block.
    /// </summary>
    static Bitmap? FromSamples(byte[] samples, int width, int height, int components)
    {
        if (components is not (1 or 3)) return null;
        if ((long)width * height * components > samples.Length) return null;

        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var locked = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[locked.Stride];
            for (int y = 0; y < height; y++)
            {
                int read = y * width * components;
                for (int x = 0; x < width; x++)
                {
                    // GDI+ is blue-green-red; PDF grey repeats its one sample.
                    byte r = samples[read + (x * components)];
                    byte g = components == 3 ? samples[read + (x * components) + 1] : r;
                    byte b = components == 3 ? samples[read + (x * components) + 2] : r;
                    row[x * 3] = b;
                    row[(x * 3) + 1] = g;
                    row[(x * 3) + 2] = r;
                }
                Marshal.Copy(row, 0, locked.Scan0 + (y * locked.Stride), locked.Stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
        return bitmap;
    }

    /// <summary>Raw samples back out of a bitmap, packed the way a PDF stores them.</summary>
    static byte[] ExtractSamples(Bitmap bitmap, int components)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var samples = new byte[(long)width * height * components];
        var locked = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[locked.Stride];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(locked.Scan0 + (y * locked.Stride), row, 0, locked.Stride);
                int write = y * width * components;
                for (int x = 0; x < width; x++)
                {
                    byte b = row[x * 3], g = row[(x * 3) + 1], r = row[(x * 3) + 2];
                    if (components == 3)
                    {
                        samples[write + (x * 3)] = r;
                        samples[write + (x * 3) + 1] = g;
                        samples[write + (x * 3) + 2] = b;
                    }
                    else
                    {
                        // The weights every luminance conversion uses; a grey
                        // image round-trips through them unchanged.
                        samples[write + x] = (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
        return samples;
    }

    /// <summary>
    /// The transparency, redrawn to the image's new size. It is sampled across
    /// the picture rather than pixel-for-pixel, so it does not have to match —
    /// but leaving a 4000-pixel mask on a 1920-pixel image would keep exactly
    /// the weight this is here to remove.
    /// </summary>
    static StoredMask? ResizeMask(StoredMask? mask, int width, int height)
    {
        if (mask is null) return null;
        using var source = FromSamples(mask.Data, mask.Width, mask.Height, components: 1);
        if (source is null) return null;

        using var resized = RedrawAt(source, width, height);
        return new StoredMask(width, height, ExtractSamples(resized, components: 1));
    }

    static Bitmap RedrawAt(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    static byte[]? EncodeJpeg(Bitmap bitmap, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        if (codec is null) return null;

        using var parameters = new EncoderParameters(1);
        using var parameter = new EncoderParameter(Encoder.Quality, (long)quality);
        parameters.Param[0] = parameter;

        using var stream = new MemoryStream();
        bitmap.Save(stream, codec, parameters);
        return stream.ToArray();
    }
}
