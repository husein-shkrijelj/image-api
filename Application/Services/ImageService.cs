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
    
    // Predefined resolutions
    private readonly Dictionary<string, (int? width, int? height)> _predefinedResolutions = new()
    {
        { "thumbnail", (160, null) },
        { "small", (320, null) },
        { "medium", (640, null) },
        { "large", (1024, null) },
        { "xlarge", (1920, null) }
    };

    public ImageService(IAzureBlobStorageService blobService, IImageRepository imageRepository)
    {
        _blobService = blobService;
        _imageRepository = imageRepository;
    }

    public async Task<ImageUploadResultDto> UploadImageAsync(IFormFile file)
    {
        try
        {
            // Validate file
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null");

            using var stream = file.OpenReadStream();
            using var image = Image.Load(stream);

            var id = Guid.NewGuid().ToString();
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            
            // Default to .png if no extension or unsupported extension
            if (string.IsNullOrEmpty(fileExtension) || 
                !new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(fileExtension))
            {
                fileExtension = ".png";
            }

            var blobName = $"original/{id}{fileExtension}";

            stream.Position = 0;
            await _blobService.UploadAsync(blobName, stream);

            var imageInfo = new ImageInfo
            {
                Id = id,
                BlobName = blobName,
                OriginalFileName = !string.IsNullOrWhiteSpace(file.FileName) ? file.FileName : $"image_{id}{fileExtension}",
                ContentType = !string.IsNullOrWhiteSpace(file.ContentType) ? file.ContentType : GetContentTypeFromExtension(fileExtension),
                OriginalHeight = image.Height,
                OriginalWidth = image.Width,
                Size = file.Length,
                UploadedAt = DateTime.UtcNow,
                FileExtension = fileExtension
            };

            await _imageRepository.AddAsync(imageInfo);

            return new ImageUploadResultDto { Id = id, Path = blobName };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to upload image: {ex.Message}", ex);
        }
    }

    // Helper method to get content type from file extension
    private string GetContentTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    public async Task<IEnumerable<ImageInfo>> GetAllImagesAsync()
    {
        return await _imageRepository.GetAllAsync();
    }

    public async Task<ImageInfo?> GetImageByIdAsync(string id)
    {
        return await _imageRepository.GetByIdAsync(id);
    }

    public async Task<ImageDownloadResultDto?> DownloadImageAsync(string id)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return null;

            // Check if blob exists before attempting download
            if (!await _blobService.ExistsAsync(info.BlobName))
                return null;

            var stream = await _blobService.DownloadAsync(info.BlobName);
            return new ImageDownloadResultDto
            {
                Stream = stream,
                ContentType = info.ContentType ?? "image/png",
                FileName = info.OriginalFileName ?? $"{id}.png"
            };
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to download image with ID: {ImageId}", id);
            return null;
        }
    }

    public async Task<ImageDownloadResultDto?> DownloadImageWithResolutionAsync(string id, string resolution)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return null;

            string blobPath;
            string fileName;

            if (resolution.ToLower() == "original")
            {
                blobPath = info.BlobName;
                fileName = info.OriginalFileName ?? $"{id}.png";
            }
            else if (_predefinedResolutions.ContainsKey(resolution.ToLower()))
            {
                var (width, height) = _predefinedResolutions[resolution.ToLower()];
                var resolutionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
                blobPath = $"resized/{id}_{resolutionSuffix}.png";
                fileName = $"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? id)}_{resolution}.png";

                // Generate if doesn't exist
                if (!await _blobService.ExistsAsync(blobPath))
                {
                    await GenerateResizedImage(info, width, height, blobPath);
                }
            }
            else
            {
                return null;
            }

            // Check if blob exists before attempting download
            if (!await _blobService.ExistsAsync(blobPath))
                return null;

            var stream = await _blobService.DownloadAsync(blobPath);
            return new ImageDownloadResultDto
            {
                Stream = stream,
                ContentType = "image/png",
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to download image with resolution. ID: {ImageId}, Resolution: {Resolution}", id, resolution);
            return null;
        }
    }

    public async Task<ImageDownloadResultDto?> GetResizedImageAsync(string id, int? width, int? height)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return null;

            // Validate dimensions
            if (width.HasValue && width > info.OriginalWidth)
                return null;
            if (height.HasValue && height > info.OriginalHeight)
                return null;

            var dimensionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
            var blobPath = $"resized/{id}_{dimensionSuffix}.png";
            var fileName = $"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? id)}_{dimensionSuffix}.png";

            // Generate if doesn't exist
            if (!await _blobService.ExistsAsync(blobPath))
            {
                await GenerateResizedImage(info, width, height, blobPath);
            }

            // Check if blob exists before attempting download
            if (!await _blobService.ExistsAsync(blobPath))
                return null;

            var stream = await _blobService.DownloadAsync(blobPath);
            return new ImageDownloadResultDto
            {
                Stream = stream,
                ContentType = "image/png",
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to get resized image. ID: {ImageId}, Width: {Width}, Height: {Height}", id, width, height);
            return null;
        }
    }

    public async Task<IEnumerable<string>?> GetAvailableResolutionsAsync(string id)
    {
        var info = await _imageRepository.GetByIdAsync(id);
        if (info == null)
            return null;

        var availableResolutions = new List<string> { "original" };

        // Check which predefined resolutions are available or can be generated
        foreach (var resolution in _predefinedResolutions)
        {
            var (width, height) = resolution.Value;
            var canGenerate = true;

            if (width.HasValue && width > info.OriginalWidth)
                canGenerate = false;
            if (height.HasValue && height > info.OriginalHeight)
                canGenerate = false;

            if (canGenerate)
                availableResolutions.Add(resolution.Key);
        }

        return availableResolutions;
    }

    public async Task<ImageUploadResultDto?> UpdateImageAsync(string id, IFormFile file)
    {
        try
        {
            var existingInfo = await _imageRepository.GetByIdAsync(id);
            if (existingInfo == null)
                return null;

            // Delete old blobs
            await DeleteImageBlobsAsync(id);

            // Upload new image
            using var stream = file.OpenReadStream();
            using var image = Image.Load(stream);

            var blobName = $"original/{id}.png";
            stream.Position = 0;
            await _blobService.UploadAsync(blobName, stream);

            // Update image info
            existingInfo.BlobName = blobName;
            existingInfo.OriginalFileName = file.FileName;
            existingInfo.ContentType = file.ContentType;
            existingInfo.OriginalHeight = image.Height;
            existingInfo.OriginalWidth = image.Width;

            await _imageRepository.UpdateAsync(existingInfo);

            return new ImageUploadResultDto { Id = id, Path = blobName };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update image with ID {id}: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteImageAsync(string id)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return false;

            // Delete all associated blobs
            await DeleteImageBlobsAsync(id);

            // Delete from database
            await _imageRepository.DeleteAsync(id);

            return true;
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to delete image with ID: {ImageId}", id);
            return false;
        }
    }

    public async Task<ResolutionGenerationResultDto?> GeneratePredefinedResolutionsAsync(string id)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return null;

            var result = new ResolutionGenerationResultDto { ImageId = id };

            foreach (var resolution in _predefinedResolutions)
            {
                var (width, height) = resolution.Value;
                var resolutionName = resolution.Key;

                // Check if resolution can be generated
                var canGenerate = true;
                if (width.HasValue && width > info.OriginalWidth)
                    canGenerate = false;
                if (height.HasValue && height > info.OriginalHeight)
                    canGenerate = false;

                if (!canGenerate)
                {
                    result.SkippedResolutions.Add($"{resolutionName} (exceeds original dimensions)");
                    continue;
                }

                var dimensionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
                var blobPath = $"resized/{id}_{dimensionSuffix}.png";

                // Skip if already exists
                if (await _blobService.ExistsAsync(blobPath))
                {
                    result.SkippedResolutions.Add($"{resolutionName} (already exists)");
                    continue;
                }

                // Generate the resolution
                try
                {
                    await GenerateResizedImage(info, width, height, blobPath);
                    result.GeneratedResolutions.Add(resolutionName);
                }
                catch (Exception ex)
                {
                    result.SkippedResolutions.Add($"{resolutionName} (error: {ex.Message})");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to generate predefined resolutions for image ID: {ImageId}", id);
            return null;
        }
    }

    // Legacy method for backward compatibility
    public async Task<string?> GetResizedImagePathAsync(string imageId, int height)
    {
        try
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
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // _logger.LogError(ex, "Failed to get resized image path for image ID: {ImageId}, Height: {Height}", imageId, height);
            return null;
        }
    }

    // Private helper methods
    private async Task GenerateResizedImage(ImageInfo info, int? width, int? height, string blobPath)
    {
        try
        {
            using var originalStream = await _blobService.DownloadAsync(info.BlobName);
            using var image = Image.Load(originalStream);

            // Calculate dimensions maintaining aspect ratio
            var (newWidth, newHeight) = CalculateNewDimensions(image.Width, image.Height, width, height);

            var resized = image.Clone(x => x.Resize(newWidth, newHeight));
            using var ms = new MemoryStream();
            resized.SaveAsPng(ms);
            ms.Position = 0;

            await _blobService.UploadAsync(blobPath, ms);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to generate resized image: {ex.Message}", ex);
        }
    }

    private (int width, int height) CalculateNewDimensions(int originalWidth, int originalHeight, int? targetWidth, int? targetHeight)
    {
        if (targetWidth.HasValue && targetHeight.HasValue)
        {
            return (targetWidth.Value, targetHeight.Value);
        }

        var aspectRatio = (double)originalWidth / originalHeight;

        if (targetWidth.HasValue)
        {
            var newHeight = (int)(targetWidth.Value / aspectRatio);
            return (targetWidth.Value, newHeight);
        }

        if (targetHeight.HasValue)
        {
            var newWidth = (int)(targetHeight.Value * aspectRatio);
            return (newWidth, targetHeight.Value);
        }

        return (originalWidth, originalHeight);
    }

    private async Task DeleteImageBlobsAsync(string id)
    {
        // Delete original image
        var info = await _imageRepository.GetByIdAsync(id);
        if (info != null)
        {
            try
            {
                await _blobService.DeleteAsync(info.BlobName);
            }
            catch
            {
                // Ignore errors for non-existent blobs
            }
        }

        // Delete all resized versions
        var blobsToDelete = new List<string>();

        // Add predefined resolution blobs
        foreach (var resolution in _predefinedResolutions)
        {
            var (width, height) = resolution.Value;
            var dimensionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
            var blobPath = $"resized/{id}_{dimensionSuffix}.png";
            blobsToDelete.Add(blobPath);
        }

        // Add legacy thumbnail path
        blobsToDelete.Add($"thumbnails/{id}_thumb.png");

        // Delete all blobs (ignore errors for non-existent blobs)
        foreach (var blobPath in blobsToDelete)
        {
            try
            {
                await _blobService.DeleteAsync(blobPath);
            }
            catch
            {
                // Ignore errors for non-existent blobs
            }
        }
    }
}