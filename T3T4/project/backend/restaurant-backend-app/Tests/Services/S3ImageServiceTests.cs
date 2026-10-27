using APIHandler.Services;

namespace APIHandler.Tests.Services;

public class S3ImageServiceTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56 }, "image/webp")]
    [InlineData(new byte[] { 0x42, 0x4D, 0x36, 0x00 }, "image/bmp")]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08 }, "image/tiff")]
    public void GetImageContentType_KnownSignature_ReturnsMimeType(byte[] data, string expected)
    {
        Assert.Equal(expected, S3ImageService.GetImageContentType(data));
    }

    [Fact]
    public void GetImageContentType_RealPngFromBase64_ReturnsPng()
    {
        // 1x1 transparent PNG, as the frontend sends it (data URL).
        const string dataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

        byte[] bytes = S3ImageService.GetImageBytesFromBase64(dataUrl);

        Assert.Equal("image/png", S3ImageService.GetImageContentType(bytes));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })] // %PDF
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 })] // RIFF....WAVE
    public void GetImageContentType_NotAnImage_Throws(byte[] data)
    {
        Assert.Throws<InvalidOperationException>(() => S3ImageService.GetImageContentType(data));
    }
}
