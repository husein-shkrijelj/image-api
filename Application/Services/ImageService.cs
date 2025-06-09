using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using ImageApi.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using ImageInfo = ImageApi.Domain.Entities.ImageInfo;

namespace ImageApi.Application.Services;

/// <summary>
/// Service for managing image operations including upload, download, resize, and resolution management.
/// </summary>
public class ImageService : IImageService
{
    #region Constants

    private static class FileExtensions
    {
        public const string Jpg = ".jpg";
        public const string Jpeg = ".jpeg";
        public const string Png = ".png";
        public const string Gif = ".gif";
        public const string Bmp = ".bmp";
        public const string Webp = ".webp";
        public const string Tiff = ".tiff";
        public const string Tif = ".tif";
    }

    private static class ContentTypes
    {
        public const string Jpeg = "image/jpeg";
        public const string Png = "image/png";
        public const string Gif = "image/gif";
        public const string Bmp = "image/bmp";
        public const string Webp = "image/webp";
    }

    private static class BlobPaths
    {
        public const string OriginalPrefix = "original";
        public const string ResizedPrefix = "resized";
        public const string ThumbnailsPrefix = "thumbnails";
        public const string OriginalSuffix = "_original";
        public const string ThumbnailSuffix = "_thumb";
        public const string HeightSuffix = "h";
        public const string WidthSuffix = "w";
        public const string PixelSuffix = "px";
    }

    private static class CacheKeys
    {
        public const string ImageInfoPrefix = "image_info_";
        public const string BlobExistsPrefix = "blob_exists_";
    }

    private static class ResolutionNames
    {
        public const string Original = "original";
        public const string Thumbnail = "thumbnail";
        public const string Small = "small";
        public const string Medium = "medium";
        public const string Large = "large";
        public const string XLarge = "xlarge";
    }

    private static class ErrorMessages
    {
        public const string FileEmptyOrNull = "File is empty or null";
        public const string ImageNotFound = "Image not found";
        public const string FailedToUpload = "Failed to upload image";
        public const string FailedToUpdate = "Failed to update image with ID";
        public const string FailedToDelete = "Failed to delete image with ID";
        public const string FailedToGenerate = "Failed to generate resized image";
        public const string FailedToDownload = "Failed to download image";
        public const string CouldNotRetrieveOriginal = "Could not retrieve original image data";
        public const string RequestedHeightTooLarge = "Requested height cannot be greater than original image height.";
    }

    #endregion

    #region Fields

    private readonly IAzureBlobStorageService _blobService;
    private readonly IImageRepository _imageRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ImageService> _logger;

    // Predefined resolutions
    private readonly Dictionary<string, (int? width, int? height)> _predefinedResolutions = new()
    {
        { ResolutionNames.Thumbnail, (160, null) },
        { ResolutionNames.Small, (320, null) },
        { ResolutionNames.Medium, (640, null) },
        { ResolutionNames.Large, (1024, null) },
        { ResolutionNames.XLarge, (1920, null) }
    };

    private readonly string[] _supportedExtensions = 
    {
        FileExtensions.Jpg, FileExtensions.Jpeg, FileExtensions.Png, 
        FileExtensions.Gif, FileExtensions.Bmp, FileExtensions.Webp, 
        FileExtensions.Tiff, FileExtensions.Tif
    };

    private readonly int[] _commonSizes = { 320, 640 };

    #endregion

    #region Constructor

    public ImageService(
        IAzureBlobStorageService blobService, 
        IImageRepository imageRepository, 
        IMemoryCache memoryCache,
        ILogger<ImageService> logger)
    {
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
        _imageRepository = imageRepository ?? throw new ArgumentNullException(nameof(imageRepository));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    public async Task<ImageUploadResultDto> UploadImageAsync(IFormFile file)
    {
        try
        {
            _logger.LogInformation("Starting image upload process for file: {FileName}", file.FileName);

            // Validate file
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Upload attempt with empty or null file");
                throw new ArgumentException(ErrorMessages.FileEmptyOrNull);
            }

            var id = Guid.NewGuid().ToString();
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

            // Default to .png if no extension or unsupported extension
            if (string.IsNullOrEmpty(fileExtension) || !_supportedExtensions.Contains(fileExtension))
            {
                fileExtension = FileExtensions.Png;
                _logger.LogDebug("Using default PNG extension for file {FileName}", file.FileName);
            }

            int imageHeight, imageWidth;
            var storageBlobName = $"{BlobPaths.OriginalPrefix}/{id}{BlobPaths.OriginalSuffix}{fileExtension}";

            // Read file content once
            using var fileStream = file.OpenReadStream();
            
            // Get dimensions without re-encoding
            using (var imageForDimensions = Image.Load(fileStream))
            {
                imageHeight = imageForDimensions.Height;
                imageWidth = imageForDimensions.Width;
            }

            // Reset stream and upload original bytes directly (preserve original format and quality)
            fileStream.Position = 0;
            await _blobService.UploadAsync(storageBlobName, fileStream);

            // Create image info record
            var imageInfo = new ImageInfo
            {
                Id = id,
                BlobName = storageBlobName,
                OriginalFileName = !string.IsNullOrWhiteSpace(file.FileName) ? file.FileName : $"image_{id}{fileExtension}",
                ContentType = !string.IsNullOrWhiteSpace(file.ContentType) ? file.ContentType : GetContentTypeFromExtension(fileExtension),
                OriginalHeight = imageHeight,
                OriginalWidth = imageWidth,
                Size = file.Length,
                UploadedAt = DateTime.UtcNow,
                FileExtension = fileExtension
            };

            // Save to database
            await _imageRepository.AddAsync(imageInfo);

            // Cache the newly created image info immediately
            CacheImageInfo(id, imageInfo, CacheItemPriority.High);

            _logger.LogInformation("Successfully uploaded image {ImageId} with dimensions {Width}x{Height}", 
                id, imageWidth, imageHeight);

            // Fire-and-forget thumbnail generation for performance (only if image is large enough)
            if (imageHeight >= 160)
            {
                _ = Task.Run(async () => await GenerateThumbnailInBackgroundOptimized(imageInfo, id, storageBlobName));
            }

            return new ImageUploadResultDto { Id = id, Path = storageBlobName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image: {ErrorMessage}", ex.Message);
            throw new InvalidOperationException($"{ErrorMessages.FailedToUpload}: {ex.Message}", ex);
        }
    }

    private async Task GenerateThumbnailInBackgroundOptimized(ImageInfo imageInfo, string id, string originalBlobName)
    {
        try
        {
            var thumbnailPath = $"{BlobPaths.ResizedPrefix}/{id}_160{BlobPaths.HeightSuffix}{FileExtensions.Png}";

            // Download the original blob we just uploaded (it's already in PNG format)
            using var originalStream = await _blobService.DownloadAsync(originalBlobName);
            using var image = Image.Load(originalStream);

            // Calculate new dimensions maintaining aspect ratio
            var aspectRatio = (double)image.Width / image.Height;
            var newWidth = (int)(160 * aspectRatio);

            // Resize and save
            var resized = image.Clone(x => x.Resize(newWidth, 160));
            using var thumbnailStream = new MemoryStream();
            resized.SaveAsPng(thumbnailStream);
            thumbnailStream.Position = 0;

            await _blobService.UploadAsync(thumbnailPath, thumbnailStream);

            // Cache that thumbnail exists
            _memoryCache.Set($"{CacheKeys.BlobExistsPrefix}{thumbnailPath}", true, TimeSpan.FromMinutes(2));
            _logger.LogInformation("Background: Pre-generated thumbnail for image {ImageId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background: Failed to pre-generate thumbnail for image {ImageId}", id);
        }
    }

    public async Task<IEnumerable<ImageInfo>> GetAllImagesAsync()
    {
        _logger.LogInformation("Retrieving all images");
        return await _imageRepository.GetAllAsync();
    }

    public async Task<ImageInfo?> GetImageByIdAsync(string id)
    {
        var cacheKey = $"{CacheKeys.ImageInfoPrefix}{id}";

        // Try to get from cache first
        if (_memoryCache.TryGetValue(cacheKey, out ImageInfo? cachedImageInfo))
        {
            _logger.LogDebug("Cache hit for image {ImageId}", id);
            return cachedImageInfo;
        }

        // Fetch from database
        var imageInfo = await _imageRepository.GetByIdAsync(id);

        // Cache the result
        if (imageInfo != null)
        {
            CacheImageInfo(id, imageInfo, CacheItemPriority.Normal);
            _logger.LogDebug("Cached image info for {ImageId}", id);
        }
        else
        {
            _logger.LogWarning("Image {ImageId} not found", id);
        }

        return imageInfo;
    }

    public async Task<ImageDownloadResultDto?> DownloadImageAsync(string id)
    {
        try
        {
            _logger.LogInformation("Downloading original image {ImageId}", id);

            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
            {
                _logger.LogWarning("Image {ImageId} not found for download", id);
                return null;
            }

            if (!await _blobService.ExistsAsync(info.BlobName))
            {
                _logger.LogWarning("Blob {BlobName} not found for image {ImageId}", info.BlobName, id);
                return null;
            }

            var blobStream = await _blobService.DownloadAsync(info.BlobName);
            var contentType = !string.IsNullOrWhiteSpace(info.ContentType) ? info.ContentType : GetContentTypeFromExtension(info.FileExtension);
            var fileName = !string.IsNullOrWhiteSpace(info.OriginalFileName) ? info.OriginalFileName : $"image_{id}{info.FileExtension}";

            _logger.LogInformation("Successfully downloaded image {ImageId}", id);

            return new ImageDownloadResultDto
            {
                Stream = blobStream,
                ContentType = contentType,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading image {ImageId}: {ErrorMessage}", id, ex.Message);
            return null;
        }
    }

    public async Task<ImageDownloadResultDto?> DownloadImageWithResolutionAsync(string id, string resolution)
    {
        try
        {
            _logger.LogInformation("Downloading image {ImageId} with resolution {Resolution}", id, resolution);

            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
            {
                _logger.LogWarning("Image {ImageId} not found", id);
                return null;
            }

            string blobPath;
            string fileName;

            if (resolution.ToLower() == ResolutionNames.Original)
            {
                return await DownloadImageAsync(id);
            }
            else if (_predefinedResolutions.ContainsKey(resolution.ToLower()))
            {
                var (width, height) = _predefinedResolutions[resolution.ToLower()];
                var resolutionSuffix = width.HasValue ? $"{width}{BlobPaths.WidthSuffix}" : $"{height}{BlobPaths.HeightSuffix}";
                blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{resolutionSuffix}{FileExtensions.Png}";
                fileName = $"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? id)}_{resolution}{FileExtensions.Png}";

                // Generate if doesn't exist
                if (!await _blobService.ExistsAsync(blobPath))
                {
                    _logger.LogInformation("Generating {Resolution} resolution for image {ImageId}", resolution, id);
                    await GenerateResizedImage(info, width, height, blobPath);
                }
            }
            else
            {
                _logger.LogWarning("Invalid resolution {Resolution} requested for image {ImageId}", resolution, id);
                return null;
            }

            if (!await _blobService.ExistsAsync(blobPath))
            {
                _logger.LogWarning("Resized image blob {BlobPath} not found", blobPath);
                return null;
            }

            var stream = await _blobService.DownloadAsync(blobPath);
            return new ImageDownloadResultDto
            {
                Stream = stream,
                ContentType = ContentTypes.Png,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading image with resolution. ID: {ImageId}, Resolution: {Resolution}", id, resolution);
            return null;
        }
    }

    public async Task<ImageDownloadResultDto?> GetResizedImageAsync(string id, int? width, int? height)
    {
        try
        {
            _logger.LogInformation("Getting resized image {ImageId} with dimensions {Width}x{Height}", id, width, height);

            var info = await GetImageByIdAsync(id);
            if (info == null)
            {
                _logger.LogWarning("Image {ImageId} not found", id);
                return null;
            }

            if (width.HasValue && width > info.OriginalWidth)
            {
                _logger.LogWarning("Requested width {Width} exceeds original width {OriginalWidth} for image {ImageId}", 
                    width, info.OriginalWidth, id);
                return null;
            }

            if (height.HasValue && height > info.OriginalHeight)
            {
                _logger.LogWarning("Requested height {Height} exceeds original height {OriginalHeight} for image {ImageId}", 
                    height, info.OriginalHeight, id);
                return null;
            }

            var dimensionSuffix = width.HasValue ? $"{width}{BlobPaths.WidthSuffix}" : $"{height}{BlobPaths.HeightSuffix}";
            var blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{dimensionSuffix}{FileExtensions.Png}";
            var fileName = $"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? id)}_{dimensionSuffix}{FileExtensions.Png}";

            if (!await _blobService.ExistsAsync(blobPath))
            {
                _logger.LogInformation("Generating resized image for {ImageId} with dimensions {Width}x{Height}", id, width, height);
                await GenerateResizedImage(info, width, height, blobPath);

                // Opportunistically generate common sizes in background
                if (height == 160)
                {
                    _ = Task.Run(async () => await GenerateCommonSizesInBackground(info, id));
                }
            }

            if (!await _blobService.ExistsAsync(blobPath))
            {
                _logger.LogError("Failed to generate or find resized image blob {BlobPath}", blobPath);
                return null;
            }

            var stream = await _blobService.DownloadAsync(blobPath);
            _logger.LogInformation("Successfully retrieved resized image {ImageId}", id);

            return new ImageDownloadResultDto
            {
                Stream = stream,
                ContentType = ContentTypes.Png,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resized image. ID: {ImageId}, Width: {Width}, Height: {Height}", id, width, height);
            return null;
        }
    }

    public async Task<IEnumerable<string>?> GetAvailableResolutionsAsync(string id)
    {
        _logger.LogInformation("Getting available resolutions for image {ImageId}", id);

        var info = await _imageRepository.GetByIdAsync(id);
        if (info == null)
        {
            _logger.LogWarning("Image {ImageId} not found", id);
            return null;
        }

        var availableResolutions = new List<string> { ResolutionNames.Original };

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

        _logger.LogInformation("Found {Count} available resolutions for image {ImageId}", availableResolutions.Count, id);
        return availableResolutions;
    }

    public async Task<ImageUploadResultDto?> UpdateImageAsync(string id, IFormFile file)
    {
        try
        {
            _logger.LogInformation("Updating image {ImageId}", id);

            var existingInfo = await _imageRepository.GetByIdAsync(id);
            if (existingInfo == null)
            {
                _logger.LogWarning("Image {ImageId} not found for update", id);
                return null;
            }

            // Delete old blobs
            await DeleteImageBlobsAsync(id);

            // Clear cache entries for the old image
            ClearImageCacheEntries(id);

            // Upload new image
            using var stream = file.OpenReadStream();
            using var image = Image.Load(stream);

            var blobName = $"{BlobPaths.OriginalPrefix}/{id}{FileExtensions.Png}";
            stream.Position = 0;
            await _blobService.UploadAsync(blobName, stream);

            // Update image info
            existingInfo.BlobName = blobName;
            existingInfo.OriginalFileName = file.FileName;
            existingInfo.ContentType = file.ContentType;
            existingInfo.OriginalHeight = image.Height;
            existingInfo.OriginalWidth = image.Width;

            await _imageRepository.UpdateAsync(existingInfo);

            // Update cache with new image info
            CacheImageInfo(id, existingInfo, CacheItemPriority.High);

            _logger.LogInformation("Successfully updated image {ImageId}", id);
            return new ImageUploadResultDto { Id = id, Path = blobName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update image {ImageId}: {ErrorMessage}", id, ex.Message);
            throw new InvalidOperationException($"{ErrorMessages.FailedToUpdate} {id}: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteImageAsync(string id)
    {
        try
        {
            _logger.LogInformation("Deleting image {ImageId}", id);

            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
            {
                _logger.LogWarning("Image {ImageId} not found for deletion", id);
                return false;
            }

            // Delete all associated blobs
            await DeleteImageBlobsAsync(id);

            // Delete from database
            await _imageRepository.DeleteAsync(id);

            // Clear cache entries for this image
            ClearImageCacheEntries(id);

            _logger.LogInformation("Successfully deleted image {ImageId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image {ImageId}: {ErrorMessage}", id, ex.Message);
            return false;
        }
    }

    public async Task<ResolutionGenerationResultDto?> GeneratePredefinedResolutionsAsync(string id)
    {
        try
        {
            _logger.LogInformation("Generating predefined resolutions for image {ImageId}", id);

            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
            {
                _logger.LogWarning("Image {ImageId} not found", id);
                return null;
            }

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
                    var skipMessage = $"{resolutionName} (exceeds original dimensions)";
                    result.SkippedResolutions.Add(skipMessage);
                    _logger.LogInformation("Skipped resolution {Resolution} for image {ImageId}: exceeds original dimensions", resolutionName, id);
                    continue;
                }

                var dimensionSuffix = width.HasValue ? $"{width}{BlobPaths.WidthSuffix}" : $"{height}{BlobPaths.HeightSuffix}";
                var blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{dimensionSuffix}{FileExtensions.Png}";

                // Skip if already exists
                if (await _blobService.ExistsAsync(blobPath))
                {
                    var skipMessage = $"{resolutionName} (already exists)";
                    result.SkippedResolutions.Add(skipMessage);
                    _logger.LogInformation("Skipped resolution {Resolution} for image {ImageId}: already exists", resolutionName, id);
                    continue;
                }

                // Generate the resolution
                try
                {
                    await GenerateResizedImage(info, width, height, blobPath);
                    result.GeneratedResolutions.Add(resolutionName);
                    _logger.LogInformation("Generated resolution {Resolution} for image {ImageId}", resolutionName, id);
                }
                catch (Exception ex)
                {
                    var errorMessage = $"{resolutionName} (error: {ex.Message})";
                    result.SkippedResolutions.Add(errorMessage);
                    _logger.LogError(ex, "Failed to generate resolution {Resolution} for image {ImageId}", resolutionName, id);
                }
            }

            _logger.LogInformation("Completed resolution generation for image {ImageId}. Generated: {GeneratedCount}, Skipped: {SkippedCount}", 
                id, result.GeneratedResolutions.Count, result.SkippedResolutions.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate predefined resolutions for image {ImageId}", id);
            return null;
        }
    }

    [Obsolete("Use GetResizedImageAsync instead. This method will be removed in a future version.")]
    public async Task<string?> GetResizedImagePathAsync(string imageId, int height)
    {
        try
        {
            _logger.LogWarning("Using deprecated method GetResizedImagePathAsync for image {ImageId}", imageId);

            var info = await _imageRepository.GetByIdAsync(imageId);
            if (info == null)
                return null;

            if (height > info.OriginalHeight)
                return null;

            if (height == 160)
            {
                var thumbnailPath = $"{BlobPaths.ThumbnailsPrefix}/{imageId}{BlobPaths.ThumbnailSuffix}{FileExtensions.Png}";
                if (await _blobService.ExistsAsync(thumbnailPath))
                    return thumbnailPath;
            }

            var variationName = $"{BlobPaths.ResizedPrefix}/{imageId}_{height}{BlobPaths.PixelSuffix}{FileExtensions.Png}";
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
            _logger.LogError(ex, "Failed to get resized image path for image {ImageId}, Height: {Height}", imageId, height);
            return null;
        }
    }

    public async Task<ResizedImageUrlResultDto?> GetResizedImageUrlAsync(string id, int height)
    {
        try
        {
            _logger.LogWarning("Using deprecated method GetResizedImageUrlAsync for image {ImageId}", id);

            var image = await _imageRepository.GetByIdAsync(id);
            if (image == null)
                return null;

            if (height > image.OriginalHeight)
            {
                return new ResizedImageUrlResultDto
                {
                    Error = ErrorMessages.RequestedHeightTooLarge
                };
            }

            // Generate the resized image if it doesn't exist
            var dimensionSuffix = $"{height}{BlobPaths.HeightSuffix}";
            var blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{dimensionSuffix}{FileExtensions.Png}";

            if (!await _blobService.ExistsAsync(blobPath))
            {
                try
                {
                    await GenerateResizedImage(image, null, height, blobPath);
                }
                catch (Exception ex)
                {
                    return new ResizedImageUrlResultDto
                    {
                        Error = $"{ErrorMessages.FailedToGenerate}: {ex.Message}"
                    };
                }
            }

            // Return URL information
            var downloadUrl = $"/api/images/{id}/resize?height={height}";
            return new ResizedImageUrlResultDto
            {
                ImageId = id,
                Height = height,
                Url = downloadUrl,
                Path = blobPath,
                Error = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetResizedImageUrlAsync for image {ImageId}", id);
            return new ResizedImageUrlResultDto
            {
                Error = $"An error occurred: {ex.Message}"
            };
        }
    }

    // Public method for testing purposes
    public async Task GenerateResizedImagePublic(ImageInfo info, int? width, int? height, string blobPath)
    {
        await GenerateResizedImage(info, width, height, blobPath);
    }

    #endregion

    #region Private Helper Methods

    private string GetContentTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            FileExtensions.Jpg or FileExtensions.Jpeg => ContentTypes.Jpeg,
            FileExtensions.Png => ContentTypes.Png,
            FileExtensions.Gif => ContentTypes.Gif,
            FileExtensions.Bmp => ContentTypes.Bmp,
            FileExtensions.Webp => ContentTypes.Webp,
            _ => ContentTypes.Png
        };
    }

    private void CacheImageInfo(string id, ImageInfo imageInfo, CacheItemPriority priority)
    {
        var cacheKey = $"{CacheKeys.ImageInfoPrefix}{id}";
        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            Priority = priority
        };
        _memoryCache.Set(cacheKey, imageInfo, cacheOptions);
    }

    private async Task GenerateThumbnailInBackground(ImageInfo imageInfo, string id)
    {
        try
        {
            var thumbnailPath = $"{BlobPaths.ResizedPrefix}/{id}_160{BlobPaths.HeightSuffix}{FileExtensions.Png}";
            await GenerateResizedImage(imageInfo, null, 160, thumbnailPath);

            // Cache that thumbnail exists
            _memoryCache.Set($"{CacheKeys.BlobExistsPrefix}{thumbnailPath}", true, TimeSpan.FromMinutes(2));
            _logger.LogInformation("Background: Pre-generated thumbnail for image {ImageId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background: Failed to pre-generate thumbnail for image {ImageId}", id);
        }
    }

    private async Task GenerateCommonSizesInBackground(ImageInfo info, string id)
    {
        foreach (var size in _commonSizes)
        {
            if (size < info.OriginalHeight)
            {
                try
                {
                    var sizePath = $"{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.HeightSuffix}{FileExtensions.Png}";

                    if (!await ExistsWithCacheAsync(sizePath))
                    {
                        await GenerateResizedImage(info, null, size, sizePath);

                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                            Priority = CacheItemPriority.Low
                        };
                        _memoryCache.Set($"{CacheKeys.BlobExistsPrefix}{sizePath}", true, cacheOptions);

                        _logger.LogInformation("Background: Generated {Size}px version for image {ImageId}", size, id);
                    }
                    else
                    {
                        _logger.LogDebug("Background: {Size}px version already exists for image {ImageId}", size, id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background: Failed to generate {Size}px for image {ImageId}", size, id);
                }
            }
            else
            {
                _logger.LogDebug("Background: Skipping {Size}px for image {ImageId} - original height ({OriginalHeight}px) is too small", 
                    size, id, info.OriginalHeight);
            }
        }
    }

    private async Task<bool> ExistsWithCacheAsync(string blobPath)
    {
        var cacheKey = $"{CacheKeys.BlobExistsPrefix}{blobPath}";

        // Try to get from cache first
        if (_memoryCache.TryGetValue(cacheKey, out bool cachedExists))
        {
            _logger.LogDebug("Cache hit for blob existence: {BlobPath} = {Exists}", blobPath, cachedExists);
            return cachedExists;
        }

        // Not in cache, check actual blob storage
        _logger.LogDebug("Cache miss for blob existence: {BlobPath} - checking storage", blobPath);
        bool actualExists = await _blobService.ExistsAsync(blobPath);

        // Cache the result with shorter expiration
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
            Priority = CacheItemPriority.Low
        };

        _memoryCache.Set(cacheKey, actualExists, cacheOptions);
        _logger.LogDebug("Cached blob existence: {BlobPath} = {Exists}", blobPath, actualExists);

        return actualExists;
    }

    private void ClearImageCacheEntries(string id)
    {
        try
        {
            // Clear image info cache
            var imageInfoCacheKey = $"{CacheKeys.ImageInfoPrefix}{id}";
            _memoryCache.Remove(imageInfoCacheKey);
            _logger.LogDebug("Cleared image info cache for {ImageId}", id);

            // Clear blob existence cache for original image patterns
            var originalBlobPatterns = new[]
            {
                $"{CacheKeys.BlobExistsPrefix}{BlobPaths.OriginalPrefix}/{id}{BlobPaths.OriginalSuffix}{FileExtensions.Png}",
                $"{CacheKeys.BlobExistsPrefix}{BlobPaths.OriginalPrefix}/{id}{FileExtensions.Png}",
                $"{CacheKeys.BlobExistsPrefix}{BlobPaths.OriginalPrefix}/{id}{FileExtensions.Jpg}",
                $"{CacheKeys.BlobExistsPrefix}{BlobPaths.OriginalPrefix}/{id}{FileExtensions.Jpeg}"
            };

            foreach (var pattern in originalBlobPatterns)
            {
                _memoryCache.Remove(pattern);
            }

            // Clear blob existence cache for all possible resized versions
            var blobCacheKeysToRemove = new List<string>();

            // Add predefined resolution blob cache keys
            foreach (var resolution in _predefinedResolutions)
            {
                var (width, height) = resolution.Value;
                var dimensionSuffix = width.HasValue ? $"{width}{BlobPaths.WidthSuffix}" : $"{height}{BlobPaths.HeightSuffix}";
                var blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{dimensionSuffix}{FileExtensions.Png}";
                blobCacheKeysToRemove.Add($"{CacheKeys.BlobExistsPrefix}{blobPath}");
            }

            // Add legacy thumbnail cache key
            blobCacheKeysToRemove.Add($"{CacheKeys.BlobExistsPrefix}{BlobPaths.ThumbnailsPrefix}/{id}{BlobPaths.ThumbnailSuffix}{FileExtensions.Png}");

            // Add common custom sizes that might be cached
            var commonSizes = new[] { 160, 320, 640, 800, 1024, 1200, 1600, 1920 };
            foreach (var size in commonSizes)
            {
                // Height-based resizes
                blobCacheKeysToRemove.Add($"{CacheKeys.BlobExistsPrefix}{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.HeightSuffix}{FileExtensions.Png}");
                // Width-based resizes  
                blobCacheKeysToRemove.Add($"{CacheKeys.BlobExistsPrefix}{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.WidthSuffix}{FileExtensions.Png}");
                // Legacy format
                blobCacheKeysToRemove.Add($"{CacheKeys.BlobExistsPrefix}{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.PixelSuffix}{FileExtensions.Png}");
            }

            // Remove all blob existence cache entries
            foreach (var cacheKey in blobCacheKeysToRemove)
            {
                _memoryCache.Remove(cacheKey);
            }

            _logger.LogDebug("Cleared {CacheEntryCount} cache entries for image {ImageId}", 
                blobCacheKeysToRemove.Count + originalBlobPatterns.Length + 1, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear cache entries for image {ImageId}", id);
        }
    }

    private async Task GenerateResizedImage(ImageInfo info, int? width, int? height, string blobPath)
    {
        try
        {
            _logger.LogDebug("Generating resized image for {ImageId} with dimensions {Width}x{Height}", info.Id, width, height);

            // First, get the original image data
            var originalImageStream = await DownloadImageAsync(info.Id);
            if (originalImageStream == null)
            {
                _logger.LogError("Could not retrieve original image data for {ImageId}", info.Id);
                throw new InvalidOperationException(ErrorMessages.CouldNotRetrieveOriginal);
            }

            // Load image from the original data
            using (originalImageStream.Stream)
            using (var image = Image.Load(originalImageStream.Stream))
            {
                // Calculate dimensions maintaining aspect ratio
                var (newWidth, newHeight) = CalculateNewDimensions(image.Width, image.Height, width, height);

                var resized = image.Clone(x => x.Resize(newWidth, newHeight));
                using var ms = new MemoryStream();
                resized.SaveAsPng(ms);
                ms.Position = 0;

                await _blobService.UploadAsync(blobPath, ms);
                _logger.LogDebug("Successfully generated resized image at {BlobPath}", blobPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate resized image for {ImageId}", info.Id);
            throw new InvalidOperationException($"{ErrorMessages.FailedToGenerate}: {ex.Message}", ex);
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
        _logger.LogDebug("Deleting all blobs for image {ImageId}", id);

        // Delete original image
        var info = await _imageRepository.GetByIdAsync(id);
        if (info != null)
        {
            try
            {
                await _blobService.DeleteAsync(info.BlobName);
                _logger.LogDebug("Deleted original blob {BlobName} for image {ImageId}", info.BlobName, id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete original blob {BlobName} for image {ImageId}", info.BlobName, id);
            }
        }

        // Delete all resized versions
        var blobsToDelete = new List<string>();

        // Add predefined resolution blobs
        foreach (var resolution in _predefinedResolutions)
        {
            var (width, height) = resolution.Value;
            var dimensionSuffix = width.HasValue ? $"{width}{BlobPaths.WidthSuffix}" : $"{height}{BlobPaths.HeightSuffix}";
            var blobPath = $"{BlobPaths.ResizedPrefix}/{id}_{dimensionSuffix}{FileExtensions.Png}";
            blobsToDelete.Add(blobPath);
        }

        // Add legacy thumbnail path
        blobsToDelete.Add($"{BlobPaths.ThumbnailsPrefix}/{id}{BlobPaths.ThumbnailSuffix}{FileExtensions.Png}");

        // Add common custom sizes
        var commonSizes = new[] { 160, 320, 640, 800, 1024, 1200, 1600, 1920 };
        foreach (var size in commonSizes)
        {
            blobsToDelete.Add($"{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.HeightSuffix}{FileExtensions.Png}");
            blobsToDelete.Add($"{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.WidthSuffix}{FileExtensions.Png}");
            blobsToDelete.Add($"{BlobPaths.ResizedPrefix}/{id}_{size}{BlobPaths.PixelSuffix}{FileExtensions.Png}");
        }

        // Delete all blobs (ignore errors for non-existent blobs)
        var deletedCount = 0;
        foreach (var blobPath in blobsToDelete)
        {
            try
            {
                await _blobService.DeleteAsync(blobPath);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete blob {BlobPath} (may not exist)", blobPath);
            }
        }

        _logger.LogDebug("Deleted {DeletedCount} blobs for image {ImageId}", deletedCount, id);
    }

    #endregion
}