namespace TravelMemory.Api.Features.PhotoImports;

internal static class SupportedPhotoMedia
{
    public static bool TryNormalize(
        string? fileName,
        string? contentType,
        out string safeFileName,
        out string normalizedContentType,
        out string extension)
    {
        safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        normalizedContentType = string.Empty;
        extension = Path.GetExtension(safeFileName).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
        {
            return false;
        }

        var suppliedContentType = contentType?.Trim().ToLowerInvariant();
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                if (suppliedContentType is not (null or "" or "image/jpeg"))
                {
                    return false;
                }

                normalizedContentType = "image/jpeg";
                return true;
            case ".heic":
                if (suppliedContentType is not (null or "" or "image/heic" or "image/heif"))
                {
                    return false;
                }

                normalizedContentType = "image/heic";
                return true;
            case ".heif":
                if (suppliedContentType is not (null or "" or "image/heic" or "image/heif"))
                {
                    return false;
                }

                normalizedContentType = "image/heif";
                return true;
            default:
                return false;
        }
    }
}
