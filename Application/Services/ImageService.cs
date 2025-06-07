using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using ImageInfo = ImageApi.Domain.Entities.ImageInfo;

namespace ImageApi.Application.Services;

public class ImageService : IImageService
{
    private readonly IAzureBlobStorageService _blobService;
    private readonly IImageRepository _imageRepository;

    public ImageService(IAzureBlobStorageService blobService, IImageRepository imageRepository)
    {
        _blobService = blobService;
        _imageRepository = imageRepository;
    }

    public async Task<ImageUploadResultDto> UploadImageAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var image = Image.Load(stream);

        var id = Guid.NewGuid().ToString();
        var blobName = $"original/{id}.png";

        stream.Position = 0;
        await _blobService.UploadAsync(blobName, stream);

        var imageInfo = new ImageInfo
        {
            Id = id,
            BlobName = blobName,
            OriginalHeight = image.Height,
            OriginalWidth = image.Width
        };
        await _imageRepository.AddAsync(imageInfo);

        return new ImageUploadResultDto { Id = id, Path = blobName };
    }

    public async Task<string?> GetResizedImagePathAsync(string imageId, int height)
    {
        var info = await _imageRepository.GetByIdAsync(imageId);
        if (info == null)
            return null;

        if (height > info.OriginalHeight)
            return null;

        if (height == 160)
        {
            var thumbnailPath = $"thumbnails/{imageId}_thumb.png";
            if (await _blobService.ExistsAsync(thumbnailPath))
                return thumbnailPath;
        }

        var variationName = $"resized/{imageId}_{height}px.png";
        if (await _blobService.ExistsAsync(variationName))
            return variationName;

        using var originalStream = await _blobService.DownloadAsync(info.BlobName);
        using var image = Image.Load(originalStream);

        var resized = image.Clone(x => x.Resize(0, height));
        using var ms = new MemoryStream();
        resized.SaveAsPng(ms);
        ms.Position = 0;

        await _blobService.UploadAsync(variationName, ms);
        return variationName;
    }

    public async Task<IEnumerable<ImageInfo>> GetAllImagesAsync()
    {
        return await _imageRepository.GetAllAsync();
    }
}