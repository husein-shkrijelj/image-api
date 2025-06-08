using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageApi.Tests.Helpers;

public static class TestImageHelper
{
    public static IFormFile CreateTestImageFile(string fileName = "test.png", int width = 800, int height = 600)
    {
        using var image = new Image<Rgba32>(width, height);

        // Fill with a simple pattern
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var color = new Rgba32((byte)(x % 256), (byte)(y % 256), 128, 255);
                image[x, y] = color;
            }
        }

        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;

        var formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        return formFile;
    }

    public static byte[] CreateTestImageBytes(int width = 800, int height = 600)
    {
        using var image = new Image<Rgba32>(width, height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var color = new Rgba32((byte)(x % 256), (byte)(y % 256), 128, 255);
                image[x, y] = color;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
