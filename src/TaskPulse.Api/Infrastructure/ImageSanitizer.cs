using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace TaskPulse.Api.Infrastructure;

public enum ImageVerdict
{
    Clean,
    NotAnImage,
    TooBig,
}

// Every uploaded image is decoded and written out again before it is stored: only the pixels survive. EXIF (GPS,
// camera, orientation - which is applied first, so nothing looks rotated), ICC, XMP, comments and any bytes hidden
// after the image data are gone, a file that merely starts with an image signature is refused, and an image larger
// than MaxPixels is refused before its pixels are allocated (decompression bombs).
public static class ImageSanitizer
{
    public const int MaxPixels = 40_000_000; // 40 MP - about 8000 × 5000
    public const int MaxSide = 12_000;

    private static readonly DecoderOptions Decoder = new() { MaxFrames = 1, SkipMetadata = false };

    public static async Task<(ImageVerdict Verdict, Stream? Output)> ReencodeAsync(Stream input, string contentType, CancellationToken cancellationToken)
    {
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(Decoder, input, cancellationToken);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return (ImageVerdict.NotAnImage, null);
        }

        if (!Matches(info.Metadata.DecodedImageFormat, contentType))
        {
            return (ImageVerdict.NotAnImage, null); // a JPEG posted as image/png, or a GIF as anything
        }

        if (info.Width > MaxSide || info.Height > MaxSide || (long)info.Width * info.Height > MaxPixels)
        {
            return (ImageVerdict.TooBig, null);
        }

        input.Position = 0;
        try
        {
            using var image = await Image.LoadAsync(Decoder, input, cancellationToken);
            image.Mutate(x => x.AutoOrient()); // bake the EXIF orientation in before the EXIF goes
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.CicpProfile = null;

            var output = new MemoryStream();
            IImageEncoder encoder = contentType.ToLowerInvariant() switch
            {
                "image/png" => new PngEncoder { CompressionLevel = PngCompressionLevel.DefaultCompression, TextCompressionThreshold = int.MaxValue, SkipMetadata = true },
                "image/jpeg" => new JpegEncoder { Quality = 90, SkipMetadata = true },
                _ => new WebpEncoder { Quality = 90, SkipMetadata = true },
            };
            await image.SaveAsync(output, encoder, cancellationToken);
            output.Position = 0;
            return (ImageVerdict.Clean, output);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException or ImageProcessingException)
        {
            return (ImageVerdict.NotAnImage, null);
        }
    }

    public static bool IsImage(string contentType) => contentType.ToLowerInvariant() is "image/png" or "image/jpeg" or "image/webp";

    private static bool Matches(IImageFormat? format, string contentType) => format is not null && string.Equals(format.DefaultMimeType, contentType, StringComparison.OrdinalIgnoreCase);
}
