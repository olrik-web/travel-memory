using System.Globalization;
using ImageMagick;

namespace TravelMemory.Worker;

internal sealed class PhotoImageProcessor
{
    private const long MaximumPixelCount = 100_000_000;
    private const uint WebMaximumDimension = 2048;
    private const uint ThumbnailMaximumDimension = 480;

    static PhotoImageProcessor()
    {
        ResourceLimits.Area = 25_000_000;
        ResourceLimits.Memory = 512UL * 1024 * 1024;
        ResourceLimits.Disk = 4UL * 1024 * 1024 * 1024;
        ResourceLimits.MaxProfileSize = 16UL * 1024 * 1024;
        ResourceLimits.Width = 20_000;
        ResourceLimits.Height = 20_000;
        ResourceLimits.ListLength = 16;
        ResourceLimits.Thread = 2;
        ResourceLimits.Time = 120;
    }

    public ImageMetadata ReadMetadata(string sourcePath)
    {
        using var image = OpenImage(sourcePath);
        var exif = image.GetExifProfile();
        var capturedAtValue =
            exif?.GetValue(ExifTag.DateTimeOriginal)?.Value
            ?? exif?.GetValue(ExifTag.DateTimeDigitized)?.Value;

        if (capturedAtValue is null
            || !DateTime.TryParseExact(
                capturedAtValue,
                "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var capturedAt))
        {
            throw new PhotoProcessingException(
                "capture_time_missing",
                "Fotoet mangler en gyldig EXIF capture time. Eksportér fotoet med metadata og importér det igen.",
                isTransient: false);
        }

        var offsetValue = exif?.GetValue(ExifTag.OffsetTimeOriginal)?.Value;
        return new ImageMetadata(
            DateTime.SpecifyKind(capturedAt, DateTimeKind.Unspecified),
            ParseOffsetMinutes(offsetValue));
    }

    public ProcessedImage Process(string sourcePath, string outputDirectory)
    {
        using var image = OpenImage(sourcePath);
        image.AutoOrient();
        image.ColorSpace = ColorSpace.sRGB;
        image.Strip();
        image.Format = MagickFormat.Jpeg;
        image.Quality = 82;

        Directory.CreateDirectory(outputDirectory);
        var webPath = Path.Combine(outputDirectory, "web.jpg");
        var thumbnailPath = Path.Combine(outputDirectory, "thumbnail.jpg");

        using var web = image.Clone();
        web.Resize(new MagickGeometry(WebMaximumDimension, WebMaximumDimension)
        {
            Greater = true,
        });
        web.Write(webPath);

        using var thumbnail = image.Clone();
        thumbnail.Resize(new MagickGeometry(ThumbnailMaximumDimension, ThumbnailMaximumDimension)
        {
            Greater = true,
        });
        thumbnail.Quality = 78;
        thumbnail.Write(thumbnailPath);

        return new ProcessedImage(
            webPath,
            thumbnailPath,
            checked((int)web.Width),
            checked((int)web.Height),
            checked((int)thumbnail.Width),
            checked((int)thumbnail.Height));
    }

    private static MagickImage OpenImage(string sourcePath)
    {
        try
        {
            var image = new MagickImage(sourcePath);
            var pixelCount = checked((long)image.Width * image.Height);
            if (pixelCount > MaximumPixelCount)
            {
                image.Dispose();
                throw new PhotoProcessingException(
                    "image_too_large",
                    "Fotoets pixelstørrelse er for stor. Eksportér en mindre kopi og importér den igen.",
                    isTransient: false);
            }

            if (image.Format is not (MagickFormat.Jpeg or MagickFormat.Heic))
            {
                image.Dispose();
                throw new PhotoProcessingException(
                    "unsupported_format",
                    "Filens indhold er ikke et understøttet JPEG- eller HEIC-foto.",
                    isTransient: false);
            }

            return image;
        }
        catch (MagickException exception)
        {
            throw new PhotoProcessingException(
                "image_decode_failed",
                "Fotoet kunne ikke afkodes. Kontrollér at filen ikke er beskadiget, og importér den igen.",
                isTransient: false,
                exception);
        }
    }

    private static int? ParseOffsetMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 6
            || value[0] is not ('+' or '-')
            || value[3] != ':'
            || !int.TryParse(value.AsSpan(1, 2), out var hours)
            || !int.TryParse(value.AsSpan(4, 2), out var minutes)
            || hours > 23
            || minutes > 59)
        {
            return null;
        }

        var total = checked(hours * 60 + minutes);
        return value[0] == '-' ? -total : total;
    }
}

internal sealed record ImageMetadata(DateTime CapturedAtOriginalLocal, int? ExifOffsetMinutes);

internal sealed record ProcessedImage(
    string WebPath,
    string ThumbnailPath,
    int Width,
    int Height,
    int ThumbnailWidth,
    int ThumbnailHeight);
