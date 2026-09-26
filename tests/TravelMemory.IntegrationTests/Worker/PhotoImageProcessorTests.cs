using TravelMemory.Worker;

namespace TravelMemory.IntegrationTests.Worker;

public sealed class PhotoImageProcessorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "travel-memory-tests",
        Guid.NewGuid().ToString("N"));

    public PhotoImageProcessorTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void Processes_heic_and_reads_original_capture_time()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic");
        var processor = new PhotoImageProcessor();

        var metadata = processor.ReadMetadata(sourcePath);
        var processed = processor.Process(sourcePath, Path.Combine(directory, "heic"));

        Assert.Equal(new DateTime(2026, 8, 17, 14, 23, 9), metadata.CapturedAtOriginalLocal);
        Assert.True(File.Exists(processed.WebPath));
        Assert.True(File.Exists(processed.ThumbnailPath));
        Assert.Equal(8, processed.Width);
        Assert.Equal(4, processed.Height);
    }

    [Fact]
    public void Applies_exif_orientation_before_writing_jpeg_derivatives()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "oriented.jpg");
        var processor = new PhotoImageProcessor();
        var metadata = processor.ReadMetadata(sourcePath);
        var processed = processor.Process(sourcePath, Path.Combine(directory, "jpeg"));

        Assert.Equal(new DateTime(2026, 8, 18, 16, 30, 0), metadata.CapturedAtOriginalLocal);
        Assert.Equal(20, processed.Width);
        Assert.Equal(40, processed.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
