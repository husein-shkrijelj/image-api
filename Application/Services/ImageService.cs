using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;
using Image = SixLabors.ImageSharp.Image;
using ImageInfo = ImageApi.Domain.Entities.ImageInfo;
using Microsoft.Extensions.Caching.Memory;

namespace ImageApi.Application.Services;

public class ImageService : IImageService
{
    private readonly IAzureBlobStorageService _blobService;
    private readonly IImageRepository _imageRepository;
    private readonly IMemoryCache _memoryCache;

    // Predefined resolutions
    private readonly Dictionary<string, (int? width, int? height)> _predefinedResolutions = new()
    {
        { "thumbnail", (160, null) },
        { "small", (320, null) },
        { "medium", (640, null) },
        { "large", (1024, null) },
        { "xlarge", (1920, null) }
    };

    public ImageService(IAzureBlobStorageService blobService, IImageRepository imageRepository, IMemoryCache memoryCache)
    {
        _blobService = blobService;
        _imageRepository = imageRepository;
        _memoryCache = memoryCache;
    }

    public async Task<ImageUploadResultDto> UploadImageAsync(IFormFile file)
    {
        try
        {
            // Validate file
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null");

            var id = Guid.NewGuid().ToString();
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

            // Default to .png if no extension or unsupported extension
            if (string.IsNullOrEmpty(fileExtension) ||
                !new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" }.Contains(fileExtension))
            {
                fileExtension = ".png";
            }

            // Read the file content into memory first
            byte[] originalFileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                originalFileBytes = memoryStream.ToArray();
            }

            // Get image dimensions
            int imageHeight, imageWidth;
            using (var imageStream = new MemoryStream(originalFileBytes))
            using (var image = Image.Load(imageStream))
            {
                imageHeight = image.Height;
                imageWidth = image.Width;
            }

            // Store image as-is (no compression)
            var storageBlobName = $"original/{id}_original.dat";
            using var originalStream = new MemoryStream(originalFileBytes);
            await _blobService.UploadAsync(storageBlobName, originalStream);


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
                FileExtension = fileExtension,
                IsCompressed = false,
                CompressionType = "none"
            };

            await _imageRepository.AddAsync(imageInfo);

            // Cache the newly created image info immediately
            var cacheKey = $"image_info_{id}";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                Priority = CacheItemPriority.High // New uploads are likely to be accessed soon
            };
            _memoryCache.Set(cacheKey, imageInfo, cacheOptions);

            // Fire-and-forget thumbnail generation for performance
            if (imageHeight >= 160)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var thumbnailPath = $"resized/{id}_160h.png";
                        await GenerateResizedImage(imageInfo, null, 160, thumbnailPath);

                        // Cache that thumbnail exists
                        _memoryCache.Set($"blob_exists_{thumbnailPath}", true, TimeSpan.FromMinutes(2));
                        Console.WriteLine($"Background: Pre-generated thumbnail for image {id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Background: Failed to pre-generate thumbnail for {id}: {ex.Message}");
                    }
                });
            }

            return new ImageUploadResultDto { Id = id, Path = storageBlobName };
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

    // Optimized GetImageByIdAsync with caching
    public async Task<ImageInfo?> GetImageByIdAsync(string id)
    {
        var cacheKey = $"image_info_{id}";

        // Try to get from cache first
        if (_memoryCache.TryGetValue(cacheKey, out ImageInfo? cachedImageInfo))
        {
            Console.WriteLine($"Cache hit for image {id}");
            return cachedImageInfo;
        }

        // Fetch from database
        var imageInfo = await _imageRepository.GetByIdAsync(id);

        // Cache the result with sliding expiration
        if (imageInfo != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10), // Reset timer on access
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), // Max 1 hour
                Priority = CacheItemPriority.Normal
            };

            _memoryCache.Set(cacheKey, imageInfo, cacheOptions);
            Console.WriteLine($"Cached image info for {id}");
        }

        return imageInfo;
    }

    public async Task<ImageDownloadResultDto?> DownloadImageAsync(string id)
    {
        try
        {
            var info = await _imageRepository.GetByIdAsync(id);
            if (info == null)
                return null;

            Console.WriteLine($"Downloading image {id} - Original format: {info.FileExtension}");

            if (!await _blobService.ExistsAsync(info.BlobName))
                return null;

            var blobStream = await _blobService.DownloadAsync(info.BlobName);

            Console.WriteLine("Returning stored data as-is (original bytes)");

            var contentType = !string.IsNullOrWhiteSpace(info.ContentType) ? info.ContentType : GetContentTypeFromExtension(info.FileExtension);
            var fileName = !string.IsNullOrWhiteSpace(info.OriginalFileName) ? info.OriginalFileName : $"image_{id}{info.FileExtension}";

            return new ImageDownloadResultDto
            {
                Stream = blobStream,
                ContentType = contentType,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading image {id}: {ex.Message}");
            return null;
        }
    }

    private async Task<Stream> DecompressBinaryFormat(Stream blobStream)
    {
        using (blobStream)
        using (var reader = new BinaryReader(blobStream))
        {
            var originalLength = reader.ReadInt32();
            var compressedLength = reader.ReadInt32();
            var compressedBytes = reader.ReadBytes(compressedLength);

            using var compressedStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);

            var originalBytes = new byte[originalLength];
            var totalRead = 0;
            int bytesRead;

            while (totalRead < originalLength && (bytesRead = await gzipStream.ReadAsync(originalBytes, totalRead, originalLength - totalRead)) > 0)
            {
                totalRead += bytesRead;
            }

            Console.WriteLine($"Decompressed {compressedBytes.Length} bytes to {totalRead} bytes");
            return new MemoryStream(originalBytes);
        }
    }

    private async Task<Stream> ConvertFromOptimizedJpeg(Stream jpegStream, string targetExtension)
    {
        using (jpegStream)
        using (var image = Image.Load(jpegStream))
        {
            var convertedStream = new MemoryStream();

            switch (targetExtension.ToLowerInvariant())
            {
                case ".png":
                    image.SaveAsPng(convertedStream);
                    break;
                case ".jpg":
                case ".jpeg":
                    var jpegEncoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder() { Quality = 95 };
                    image.SaveAsJpeg(convertedStream, jpegEncoder);
                    break;
                case ".gif":
                    image.SaveAsGif(convertedStream);
                    break;
                case ".bmp":
                    image.SaveAsBmp(convertedStream);
                    break;
                case ".webp":
                    image.SaveAsWebp(convertedStream);
                    break;
                default:
                    image.SaveAsPng(convertedStream);
                    break;
            }

            convertedStream.Position = 0;
            Console.WriteLine($"Converted optimized JPEG to {targetExtension}, size: {convertedStream.Length} bytes");
            return convertedStream;
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
                // Download original file
                return await DownloadImageAsync(id);
            }
            else if (_predefinedResolutions.ContainsKey(resolution.ToLower()))
            {
                var (width, height) = _predefinedResolutions[resolution.ToLower()];
                var resolutionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
                blobPath = $"resized/{id}_{resolutionSuffix}.png"; // Resized versions are always PNG
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
                ContentType = "image/png", // Resized versions are always PNG
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading image with resolution. ID: {id}, Resolution: {resolution}, Error: {ex.Message}");
            return null;
        }
    }

    // Optimized resize with parallel common sizes generation
    public async Task<ImageDownloadResultDto?> GetResizedImageAsync(string id, int? width, int? height)
    {
        try
        {
            var info = await GetImageByIdAsync(id); // Uses cached version
            if (info == null)
                return null;

            if (width.HasValue && width > info.OriginalWidth)
                return null;
            if (height.HasValue && height > info.OriginalHeight)
                return null;

            var dimensionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
            var blobPath = $"resized/{id}_{dimensionSuffix}.png";
            var fileName = $"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? id)}_{dimensionSuffix}.png";

            // Check if exists first (fast check)
            if (!await _blobService.ExistsAsync(blobPath))
            {
                await GenerateResizedImage(info, width, height, blobPath);

                // Opportunistically generate common sizes in background
                if (height == 160) // If thumbnail was requested, generate other common sizes
                {
                    _ = Task.Run(async () =>
                    {
                        await GenerateCommonSizesInBackground(info, id);
                    });
                }
            }

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

    // Background generation of common sizes
    private async Task GenerateCommonSizesInBackground(ImageInfo info, string id)
    {
        var commonSizes = new[] { 320, 640 }; // small and medium sizes

        foreach (var size in commonSizes)
        {
            if (size < info.OriginalHeight) // Only generate if original is larger
            {
                try
                {
                    var sizePath = $"resized/{id}_{size}h.png";

                    // Check if it already exists (with cache)
                    if (!await ExistsWithCacheAsync(sizePath))
                    {
                        await GenerateResizedImage(info, null, size, sizePath);

                        // Cache that this size now exists
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                            Priority = CacheItemPriority.Low
                        };
                        _memoryCache.Set($"blob_exists_{sizePath}", true, cacheOptions);

                        Console.WriteLine($"Background: Generated {size}px version for {id}");
                    }
                    else
                    {
                        Console.WriteLine($"Background: {size}px version already exists for {id}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Background: Failed to generate {size}px for {id}: {ex.Message}");
                    // Continue with next size even if one fails
                }
            }
            else
            {
                Console.WriteLine($"Background: Skipping {size}px for {id} - original height ({info.OriginalHeight}px) is too small");
            }
        }
    }

    // Cache blob existence checks for better performance
    private async Task<bool> ExistsWithCacheAsync(string blobPath)
    {
        var cacheKey = $"blob_exists_{blobPath}";

        // Try to get from cache first
        if (_memoryCache.TryGetValue(cacheKey, out bool cachedExists))
        {
            Console.WriteLine($"Cache hit for blob existence: {blobPath} = {cachedExists}");
            return cachedExists;
        }

        // Not in cache, check actual blob storage
        Console.WriteLine($"Cache miss for blob existence: {blobPath} - checking storage");
        bool actualExists = await _blobService.ExistsAsync(blobPath);

        // Cache the result with shorter expiration (blobs can be created/deleted frequently)
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2), // Short expiration for blob existence
            Priority = CacheItemPriority.Low // Lower priority than image info
        };

        _memoryCache.Set(cacheKey, actualExists, cacheOptions);
        Console.WriteLine($"Cached blob existence: {blobPath} = {actualExists}");

        return actualExists;
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

            // Clear cache entries for the old image
            ClearImageCacheEntries(id);

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

            // Update cache with new image info
            var cacheKey = $"image_info_{id}";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                Priority = CacheItemPriority.High
            };
            _memoryCache.Set(cacheKey, existingInfo, cacheOptions);

            return new ImageUploadResultDto { Id = id, Path = blobName };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update image with ID {id}: {ex.Message}", ex);
        }
    }

    private void ClearImageCacheEntries(string id)
    {
        try
        {
            // Clear image info cache
            var imageInfoCacheKey = $"image_info_{id}";
            _memoryCache.Remove(imageInfoCacheKey);
            Console.WriteLine($"Cleared image info cache for {id}");

            // Clear blob existence cache for original image
            // Note: We can't easily get the original blob name here since we might be deleting
            // So we'll clear common patterns
            var originalBlobPatterns = new[]
            {
            $"blob_exists_original/{id}_original.dat",
            $"blob_exists_original/{id}.png",
            $"blob_exists_original/{id}.jpg",
            $"blob_exists_original/{id}.jpeg"
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
                var dimensionSuffix = width.HasValue ? $"{width}w" : $"{height}h";
                var blobPath = $"resized/{id}_{dimensionSuffix}.png";
                blobCacheKeysToRemove.Add($"blob_exists_{blobPath}");
            }

            // Add legacy thumbnail cache key
            blobCacheKeysToRemove.Add($"blob_exists_thumbnails/{id}_thumb.png");

            // Add common custom sizes that might be cached
            var commonSizes = new[] { 160, 320, 640, 800, 1024, 1200, 1600, 1920 };
            foreach (var size in commonSizes)
            {
                // Height-based resizes
                blobCacheKeysToRemove.Add($"blob_exists_resized/{id}_{size}h.png");
                // Width-based resizes  
                blobCacheKeysToRemove.Add($"blob_exists_resized/{id}_{size}w.png");
                // Legacy format
                blobCacheKeysToRemove.Add($"blob_exists_resized/{id}_{size}px.png");
            }

            // Remove all blob existence cache entries
            foreach (var cacheKey in blobCacheKeysToRemove)
            {
                _memoryCache.Remove(cacheKey);
            }

            Console.WriteLine($"Cleared {blobCacheKeysToRemove.Count + originalBlobPatterns.Length + 1} cache entries for image {id}");
        }
        catch (Exception ex)
        {
            // Don't fail the delete operation if cache clearing fails
            Console.WriteLine($"Warning: Failed to clear cache entries for {id}: {ex.Message}");
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

            // Clear cache entries for this image
            ClearImageCacheEntries(id);

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
            // First, get the original image data
            var originalImageStream = await DownloadImageAsync(info.Id);
            if (originalImageStream == null)
                throw new InvalidOperationException("Could not retrieve original image data");

            // Load image from the original data
            using (originalImageStream.Stream)
            using (var image = Image.Load(originalImageStream.Stream))
            {
                // Calculate dimensions maintaining aspect ratio
                var (newWidth, newHeight) = CalculateNewDimensions(image.Width, image.Height, width, height);

                var resized = image.Clone(x => x.Resize(newWidth, newHeight));
                using var ms = new MemoryStream();
                resized.SaveAsPng(ms); // Save resized versions as PNG for consistency
                ms.Position = 0;

                await _blobService.UploadAsync(blobPath, ms);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to generate resized image: {ex.Message}", ex);
        }
    }

    public async Task GenerateResizedImagePublic(ImageInfo info, int? width, int? height, string blobPath)
    {
        await GenerateResizedImage(info, width, height, blobPath);
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

    public async Task<ResizedImageUrlResultDto?> GetResizedImageUrlAsync(string id, int height)
    {
        try
        {
            var image = await _imageRepository.GetByIdAsync(id);
            if (image == null)
                return null;

            if (height > image.OriginalHeight)
                return new ResizedImageUrlResultDto
                {
                    Error = "Requested height cannot be greater than original image height."
                };

            // Generate the resized image if it doesn't exist
            var dimensionSuffix = $"{height}h";
            var blobPath = $"resized/{id}_{dimensionSuffix}.png";

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
                        Error = $"Failed to generate resized image: {ex.Message}"
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
            return new ResizedImageUrlResultDto
            {
                Error = $"An error occurred: {ex.Message}"
            };
        }
    }
}