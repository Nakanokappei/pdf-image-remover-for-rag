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
    /// <summary>
    /// The picture redrawn no wider than <paramref name="maximumWidth"/>, still
    /// as a PNG, or the bytes as they came when it is already no wider. The
    /// same one-way guarantee the reduction pass makes: a resolution setting
    /// can take pixels away and can never invent them.
    ///
    /// Here so the settings window can show what a resolution costs through the
    /// resampler that a save runs, rather than through a second copy of it.
    /// </summary>
    public byte[] RedrawNoWiderThan(byte[] png, int maximumWidth)
    {
        try
        {
            using var source = new MemoryStream(png);
            using var picture = new Bitmap(source);
            if (picture.Width <= maximumWidth) return png;

            int height = Math.Max(
                1, (int)Math.Round(picture.Height * (maximumWidth / (double)picture.Width)));
            using var resized = RedrawAt(picture, maximumWidth, height);

            using var output = new MemoryStream();
            resized.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception)
        {
            // A picture that will not decode is one to leave alone; the caller
            // has working bytes either way.
            return png;
        }
    }

    /// <summary>
    /// The picture re-encoded as a JPEG, or the bytes as they came if the
    /// encoder will not take it. What <see cref="Resize"/> does to an image the
    /// PDF already stores as JPEG, reached from bytes instead of a bitmap so
    /// the settings window can show it.
    /// </summary>
    public byte[] ReencodeAsJpeg(byte[] png, int jpegQuality)
    {
        try
        {
            using var source = new MemoryStream(png);
            using var picture = new Bitmap(source);

            // Onto white without an alpha channel, for the reason
            // ChooseStorageForm redraws: the encoder will not take one.
            using var opaque = new Bitmap(
                picture.Width, picture.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(opaque))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(
                    picture, new Rectangle(0, 0, picture.Width, picture.Height));
            }

            return EncodeJpeg(opaque, jpegQuality) ?? png;
        }
        catch (Exception)
        {
            return png;
        }
    }

    public byte[] ChooseStorageForm(byte[] renderedPng, int jpegQuality)
    {
        try
        {
            using var source = new MemoryStream(renderedPng);
            using var picture = new Bitmap(source);

            // Composited onto white, and the transparency is simply spent.
            //
            // It used to be a reason to keep the whole picture lossless, since
            // JPEG has no alpha channel and a flattened region is transparent
            // wherever its objects draw nothing. Measured on a customer's file
            // (2026-08-18), nine of twenty-seven flattened regions had a
            // transparent pixel somewhere - an antialiased edge is enough - and
            // those nine cost 2,402 KB against 534 KB for the fifteen that
            // became JPEGs, for the same number of pixels. The output came out
            // LARGER than the file it cleaned.
            //
            // What this spends is the ability to see through the rectangle to a
            // colored page background or to something drawn under it. This is a
            // tool for feeding a retrieval pipeline, and the user judged that
            // not worth 4.5 times the bytes.
            //
            // The destination rectangle is spelled out, and that is not
            // decoration. DrawImageUnscaled draws at the picture's PHYSICAL
            // size, so it scales by the ratio of the two resolutions - and GDI+
            // stamps a new bitmap with the display's dpi, which is 192 on a
            // 200% screen against a picture authored at 96. Only the top-left
            // quarter of it would survive.
            using var opaque = new Bitmap(
                picture.Width, picture.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(opaque))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(
                    picture, new Rectangle(0, 0, picture.Width, picture.Height));
            }

            // Whatever it comes to. This used to keep the PNG when it was the
            // smaller of the two, which made a flattened diagram lossless and
            // saved a few kilobytes - and made the quality setting a control
            // that did nothing on the very pictures it was written for. The
            // user asked for the setting to be obeyed (2026-08-18), so it is.
            return EncodeJpeg(opaque, jpegQuality) ?? renderedPng;
        }
        catch (Exception)
        {
            // A picture that will not decode is one to store as it came. The
            // caller has a working PNG either way, so there is nothing here
            // worth failing a save over.
            return renderedPng;
        }
    }

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
    /// row, while GDI+ wants a stride it chooses and color components the other
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
                    // GDI+ is blue-green-red; PDF gray repeats its one sample.
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
                        // The weights every luminance conversion uses; a gray
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

    /// <summary>
    /// Internal rather than private so the settings window can show what a
    /// quality setting does to a picture. One encoder for both, or the preview
    /// would be a claim about a different encoder than the one that runs.
    /// </summary>
    internal static byte[]? EncodeJpeg(Bitmap bitmap, int quality)
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
