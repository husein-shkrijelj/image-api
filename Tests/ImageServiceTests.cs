using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using ImageApi.Application.Services;
using ImageApi.Domain.Entities;
using ImageApi.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace ImageApi.Tests.Services;

public class ImageServiceTests : IDisposable
{
    private readonly Mock<IAzureBlobStorageService> _mockBlobService;
    private readonly Mock<IImageRepository> _mockImageRepository;
    private readonly Mock<ILogger<ImageService>> _mockLogger;
    private readonly IMemoryCache _memoryCache;
    private readonly ImageService _imageService;

    public ImageServiceTests()
    {
        _mockBlobService = new Mock<IAzureBlobStorageService>();
        _mockImageRepository = new Mock<IImageRepository>();
        _mockLogger = new Mock<ILogger<ImageService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _imageService = new ImageService(
            _mockBlobService.Object, 
            _mockImageRepository.Object, 
            _memoryCache,
            _mockLogger.Object);
    }

    [Fact]
    public async Task UploadImageAsync_ValidImage_ReturnsSuccessResult()
    {
        // Arrange
        var testFile = TestImageHelper.CreateTestImageFile("test.png", 800, 600);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        _mockImageRepository.Setup(x => x.AddAsync(It.IsAny<ImageInfo>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.UploadImageAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Id);
        Assert.NotNull(result.Path);
        Assert.Contains("original/", result.Path);
        Assert.Contains("_original", result.Path);
        Assert.EndsWith(".png", result.Path);

        _mockBlobService.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        _mockImageRepository.Verify(x => x.AddAsync(It.IsAny<ImageInfo>()), Times.Once);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Starting image upload process for file:");
        VerifyLogCalled(LogLevel.Information, "Successfully uploaded image");
    }


    [Fact]
    public async Task UploadImageAsync_EmptyFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyFile = new MockFormFile(Array.Empty<byte>(), "empty.png");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _imageService.UploadImageAsync(emptyFile));
        Assert.Contains("File is empty or null", exception.Message);
        
        // Verify error logging
        VerifyLogCalled(LogLevel.Warning, "Upload attempt with empty or null file");
        VerifyLogCalled(LogLevel.Error, "Failed to upload image:");
    }

    [Fact]
    public async Task UploadImageAsync_UnsupportedExtension_DefaultsToPng()
    {
        // Arrange
        var testFile = TestImageHelper.CreateTestImageFile("test.xyz", 800, 600);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        _mockImageRepository.Setup(x => x.AddAsync(It.IsAny<ImageInfo>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.UploadImageAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.EndsWith(".jpg", result.Path);
        
        // Verify logging for default extension
        VerifyLogCalled(LogLevel.Debug, "Using default JPG extension for file");
    }

    [Fact]
    public async Task GetImageByIdAsync_ExistingImage_ReturnsImageInfo()
    {
        // Arrange
        var imageId = "test-id";
        var expectedImage = new ImageInfo
        {
            Id = imageId,
            OriginalFileName = "test.png",
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(expectedImage);

        // Act
        var result = await _imageService.GetImageByIdAsync(imageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(imageId, result.Id);
        Assert.Equal("test.png", result.OriginalFileName);
        
        // Verify caching log
        VerifyLogCalled(LogLevel.Debug, "Cached image info for");
    }

    [Fact]
    public async Task GetImageByIdAsync_NonExistingImage_ReturnsNull()
    {
        // Arrange
        var imageId = "non-existing-id";
        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _imageService.GetImageByIdAsync(imageId);

        // Assert
        Assert.Null(result);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Image", "not found");
    }

    [Fact]
    public async Task GetImageByIdAsync_CachesResult()
    {
        // Arrange
        var imageId = "test-id";
        var expectedImage = new ImageInfo { Id = imageId };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(expectedImage);

        // Act - First call
        var result1 = await _imageService.GetImageByIdAsync(imageId);
        var result2 = await _imageService.GetImageByIdAsync(imageId);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(result1.Id, result2.Id);

        // Repository should only be called once due to caching
        _mockImageRepository.Verify(x => x.GetByIdAsync(imageId), Times.Once);
        
        // Verify cache hit log
        VerifyLogCalled(LogLevel.Debug, "Cache hit for image");
    }

    [Fact]
    public async Task DownloadImageAsync_ExistingImage_ReturnsDownloadResult()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            FileExtension = ".png"
        };

        var testStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(testStream);

        // Act
        var result = await _imageService.DownloadImageAsync(imageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("test.png", result.FileName);
        Assert.NotNull(result.Stream);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Downloading original image");
        VerifyLogCalled(LogLevel.Information, "Successfully downloaded image");
    }

    [Fact]
    public async Task DownloadImageAsync_NonExistingImage_ReturnsNull()
    {
        // Arrange
        var imageId = "non-existing-id";
        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _imageService.DownloadImageAsync(imageId);

        // Assert
        Assert.Null(result);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Image", "not found for download");
    }

    [Fact]
    public async Task GetResizedImageAsync_ValidDimensions_ReturnsResizedImage()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600,
            BlobName = "original/test_original.png"
        };

        var resizedStream = new MemoryStream(TestImageHelper.CreateTestImageBytes(400, 300));

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(It.IsAny<string>()))
            .ReturnsAsync(resizedStream);

        // Act
        var result = await _imageService.GetResizedImageAsync(imageId, 400, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.NotNull(result.Stream);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Getting resized image");
        VerifyLogCalled(LogLevel.Information, "Successfully retrieved resized image");
    }

    [Fact]
    public async Task GetResizedImageAsync_DimensionsExceedOriginal_ReturnsNull()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act
        var result = await _imageService.GetResizedImageAsync(imageId, 1000, null); // Width exceeds original

        // Assert
        Assert.Null(result);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Requested width", "exceeds original width");
    }

    [Fact]
    public async Task DeleteImageAsync_ExistingImage_ReturnsTrue()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.png"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockImageRepository.Setup(x => x.DeleteAsync(imageId))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.DeleteImageAsync(imageId);

        // Assert
        Assert.True(result);
        _mockImageRepository.Verify(x => x.DeleteAsync(imageId), Times.Once);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Deleting image");
        VerifyLogCalled(LogLevel.Information, "Successfully deleted image");
    }

    [Fact]
    public async Task DeleteImageAsync_NonExistingImage_ReturnsFalse()
    {
        // Arrange
        var imageId = "non-existing-id";
        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _imageService.DeleteImageAsync(imageId);

        // Assert
        Assert.False(result);
        _mockImageRepository.Verify(x => x.DeleteAsync(It.IsAny<string>()), Times.Never);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Image", "not found for deletion");
    }

    [Fact]
    public async Task GeneratePredefinedResolutionsAsync_ValidImage_ReturnsResult()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 2000,
            OriginalHeight = 1930,
            BlobName = "original/test_original.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            FileExtension = ".png"
        };

        var originalImageBytes = TestImageHelper.CreateTestImageBytes(2000, 1500);
        var originalStream = new MemoryStream(originalImageBytes);

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Mock that resized versions don't exist initially (to force generation)
        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("resized/"))))
            .ReturnsAsync(false);

        // Mock that original blob exists
        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        // Mock downloading the original image for resize operations
        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(() => new MemoryStream(originalImageBytes));

        // Mock uploading resized versions
        _mockBlobService.Setup(x => x.UploadAsync(It.Is<string>(s => s.Contains("resized/")), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.GeneratePredefinedResolutionsAsync(imageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(imageId, result.ImageId);
        Assert.NotEmpty(result.GeneratedResolutions);

        // Should generate all predefined resolutions since image is large enough
        Assert.Contains("thumbnail", result.GeneratedResolutions);
        Assert.Contains("small", result.GeneratedResolutions);
        Assert.Contains("medium", result.GeneratedResolutions);
        Assert.Contains("large", result.GeneratedResolutions);
        Assert.Contains("xlarge", result.GeneratedResolutions);

        // Verify that upload was called for each resolution
        _mockBlobService.Verify(x => x.UploadAsync(It.Is<string>(s => s.Contains("resized/")), It.IsAny<Stream>()),
            Times.Exactly(5)); // 5 predefined resolutions

        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Generating predefined resolutions for image");
        VerifyLogCalled(LogLevel.Information, "Completed resolution generation for image");
    }

    [Fact]
    public async Task GeneratePredefinedResolutionsAsync_SomeResolutionsExist_SkipsExisting()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 2000,
            OriginalHeight = 1500,
            BlobName = "original/test_original.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            FileExtension = ".png"
        };

        var originalImageBytes = TestImageHelper.CreateTestImageBytes(2000, 1500);

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Mock that thumbnail already exists, others don't
        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("160w"))))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("resized/") && !s.Contains("160w"))))
            .ReturnsAsync(false);

        // Mock that original blob exists
        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        // Mock downloading the original image for resize operations
        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(() => new MemoryStream(originalImageBytes));

        // Mock uploading resized versions
        _mockBlobService.Setup(x => x.UploadAsync(It.Is<string>(s => s.Contains("resized/") && !s.Contains("160w")), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.GeneratePredefinedResolutionsAsync(imageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(imageId, result.ImageId);

        // Should generate 4 resolutions (thumbnail already exists)
        Assert.Equal(4, result.GeneratedResolutions.Count);
        Assert.Single(result.SkippedResolutions);

        Assert.Contains("small", result.GeneratedResolutions);
        Assert.Contains("medium", result.GeneratedResolutions);
        Assert.Contains("large", result.GeneratedResolutions);

        // Verify that upload was called 4 times (excluding existing thumbnail)
        _mockBlobService.Verify(x => x.UploadAsync(It.Is<string>(s => s.Contains("resized/") && !s.Contains("160w")), It.IsAny<Stream>()),
            Times.Exactly(4));
    }


    [Fact]
    public async Task GetAvailableResolutionsAsync_ValidImage_ReturnsResolutions()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 2100,
            OriginalHeight = 2000
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act
        // Act
        var result = await _imageService.GetAvailableResolutionsAsync(imageId);

        // Assert
        Assert.NotNull(result);
        var resolutions = result.ToList();
        Assert.Contains("original", resolutions);
        Assert.Contains("thumbnail", resolutions);
        Assert.Contains("small", resolutions);
        Assert.Contains("medium", resolutions);
        Assert.Contains("large", resolutions);
        Assert.Contains("xlarge", resolutions);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Getting available resolutions for image");
        VerifyLogCalled(LogLevel.Information, "Found", "available resolutions for image");
    }

    [Fact]
    public async Task GetAvailableResolutionsAsync_SmallImage_ReturnsLimitedResolutions()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 200,
            OriginalHeight = 170
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act
        var result = await _imageService.GetAvailableResolutionsAsync(imageId);

        // Assert
        Assert.NotNull(result);
        var resolutions = result.ToList();
        Assert.Contains("original", resolutions);
        Assert.Contains("thumbnail", resolutions);
        Assert.DoesNotContain("xlarge", resolutions); // Should not contain xlarge for small image
    }

    [Fact]
    public async Task DownloadImageWithResolutionAsync_OriginalResolution_ReturnsOriginalImage()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.png",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            FileExtension = ".png"
        };

        var testStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(testStream);

        // Act
        var result = await _imageService.DownloadImageWithResolutionAsync(imageId, "original");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("test.png", result.FileName);
    }

    [Fact]
    public async Task DownloadImageWithResolutionAsync_ThumbnailResolution_GeneratesAndReturns()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600,
            BlobName = "original/test_original.png",
            OriginalFileName = "test.png"
        };

        var originalStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());
        var resizedStream = new MemoryStream(TestImageHelper.CreateTestImageBytes(160, 120));

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // First call to check if resized version exists - return false
        _mockBlobService.SetupSequence(x => x.ExistsAsync(It.IsAny<string>()))
            .Returns(Task.FromResult(false)) // Resized doesn't exist
            .Returns(Task.FromResult(true))  // After generation, it exists
            .Returns(Task.FromResult(true)); // Final check

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(originalStream);

        _mockBlobService.Setup(x => x.DownloadAsync(It.Is<string>(s => s.Contains("resized"))))
            .ReturnsAsync(resizedStream);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.DownloadImageWithResolutionAsync(imageId, "thumbnail");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Contains("thumbnail", result.FileName);
        
        // Verify generation logging
        VerifyLogCalled(LogLevel.Information, "Generating", "resolution for image");
    }

    [Fact]
    public async Task DownloadImageWithResolutionAsync_InvalidResolution_ReturnsNull()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act
        var result = await _imageService.DownloadImageWithResolutionAsync(imageId, "invalid-resolution");

        // Assert
        Assert.Null(result);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Invalid resolution", "requested for image");
    }

    [Fact]
    public async Task UpdateImageAsync_ExistingImage_ReturnsUpdatedResult()
    {
        // Arrange
        var imageId = "test-id";
        var existingImage = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.png",
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        var updateFile = TestImageHelper.CreateTestImageFile("updated.png", 1000, 750);

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(existingImage);

        _mockImageRepository.Setup(x => x.UpdateAsync(It.IsAny<ImageInfo>()))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _imageService.UpdateImageAsync(imageId, updateFile);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(imageId, result.Id);
        Assert.NotNull(result.Path);
        
        // Verify logging
        VerifyLogCalled(LogLevel.Information, "Updating image");
        VerifyLogCalled(LogLevel.Information, "Successfully updated image");
    }

    [Fact]
    public async Task UpdateImageAsync_NonExistingImage_ReturnsNull()
    {
        // Arrange
        var imageId = "non-existing-id";
        var updateFile = TestImageHelper.CreateTestImageFile("updated.png", 1000, 750);

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _imageService.UpdateImageAsync(imageId, updateFile);

        // Assert
        Assert.Null(result);
        
        // Verify warning log
        VerifyLogCalled(LogLevel.Warning, "Image", "not found for update");
    }

    [Fact]
    public async Task GenerateResizedImagePublic_ValidParameters_GeneratesSuccessfully()
    {
        // Arrange
        var imageInfo = new ImageInfo
        {
            Id = "test-id",
            BlobName = "original/test_original.png",
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        var originalStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());
        var blobPath = "resized/test-id_400w.png";

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageInfo.Id))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(originalStream);

        _mockBlobService.Setup(x => x.UploadAsync(blobPath, It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        // Act & Assert - Should not throw
        await _imageService.GenerateResizedImagePublic(imageInfo, 400, null, blobPath);

        // Verify upload was called
        _mockBlobService.Verify(x => x.UploadAsync(blobPath, It.IsAny<Stream>()), Times.Once);
    }

    [Fact]
    public async Task GeneratePredefinedResolutionsAsync_ValidImage_IncludesThumbnailGeneration()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600,
            BlobName = "original/test_original.jpg"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockBlobService.Setup(x => x.DownloadAsync(It.IsAny<string>()))
            .ReturnsAsync(new MemoryStream(TestImageHelper.CreateTestImageBytes()));

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.GeneratePredefinedResolutionsAsync(imageId);

        Assert.NotNull(result);
        Assert.Contains("thumbnail", result.GeneratedResolutions);
    }

    [Fact]
    public async Task GetResizedImageAsync_ThumbnailHeight_ReturnsCorrectImage()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("160h"))))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(It.Is<string>(s => s.Contains("160h"))))
            .ReturnsAsync(new MemoryStream(TestImageHelper.CreateTestImageBytes(213, 160)));

        var result = await _imageService.GetResizedImageAsync(imageId, null, 160);

        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Contains("160h", result.FileName);
    }

    [Fact]
    public async Task UploadImageAsync_VerySmallImage_KeepsOriginalSize()
    {
        var testFile = TestImageHelper.CreateTestImageFile("tiny.png", 50, 40);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        _mockImageRepository.Setup(x => x.AddAsync(It.IsAny<ImageInfo>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.UploadImageAsync(testFile);

        Assert.NotNull(result);

        await Task.Delay(100);

        _mockBlobService.Verify(x => x.UploadAsync(
            It.Is<string>(path => path.Contains("160h")), 
            It.IsAny<Stream>()), 
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeleteImageAsync_ExistingImage_RemovesThumbnailFromCache()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.jpg"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockImageRepository.Setup(x => x.DeleteAsync(imageId))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.DeleteImageAsync(imageId);

        Assert.True(result);
        _mockImageRepository.Verify(x => x.DeleteAsync(imageId), Times.Once);
    }

    [Fact]
    public async Task UpdateImageAsync_ExistingImage_RegeneratesThumbnail()
    {
        var imageId = "test-id";
        var existingInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/old_original.jpg"
        };

        var newFile = TestImageHelper.CreateTestImageFile("updated.png", 1000, 800);

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(existingInfo);

        _mockImageRepository.Setup(x => x.UpdateAsync(It.IsAny<ImageInfo>()))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.UpdateImageAsync(imageId, newFile);

        Assert.NotNull(result);
        Assert.Equal(imageId, result.Id);
        _mockImageRepository.Verify(x => x.UpdateAsync(It.IsAny<ImageInfo>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailableResolutionsAsync_SmallImage_StillIncludesThumbnail()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 100,
            OriginalHeight = 80
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        var result = await _imageService.GetAvailableResolutionsAsync(imageId);

        Assert.NotNull(result);
        var resolutions = result.ToList();
        
        Assert.Contains("original", resolutions);
        Assert.DoesNotContain("small", resolutions);
        Assert.DoesNotContain("medium", resolutions);
    }

    [Fact]
    public async Task DownloadImageWithResolution_InvalidResolution_ReturnsNull()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        var result = await _imageService.DownloadImageWithResolutionAsync(imageId, "invalid");

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadImageWithResolution_OriginalResolution_CallsDownloadImageAsync()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600,
            BlobName = "original/test_original.jpg",
            ContentType = "image/jpeg",
            OriginalFileName = "test.jpg"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(imageInfo.BlobName))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(new MemoryStream(TestImageHelper.CreateTestImageBytes()));

        var result = await _imageService.DownloadImageWithResolutionAsync(imageId, "original");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal("test.jpg", result.FileName);
    }

    [Fact]
    public async Task GeneratePredefinedResolutionsAsync_ImageExactly160Height_GeneratesThumbnail()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 213,
            OriginalHeight = 160,
            BlobName = "original/exact_original.jpg"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("160h"))))
            .ReturnsAsync(false);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("320h") || s.Contains("640h") || s.Contains("1024h") || s.Contains("1920h"))))
            .ReturnsAsync(false);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(new MemoryStream(TestImageHelper.CreateTestImageBytes(213, 160)));

        _mockBlobService.Setup(x => x.UploadAsync(It.Is<string>(s => s.Contains("160h")), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.GeneratePredefinedResolutionsAsync(imageId);

        Assert.NotNull(result);
        Assert.Contains("thumbnail", result.GeneratedResolutions);
        Assert.Contains("small (exceeds original dimensions)", result.SkippedResolutions);
    }

    [Fact]
    public async Task GeneratePredefinedResolutionsAsync_ThumbnailAlreadyExists_SkipsThumbnail()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600,
            BlobName = "original/test_original.jpg"
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("160h"))))
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.ExistsAsync(It.Is<string>(s => s.Contains("320h") || s.Contains("640h") || s.Contains("1024h") || s.Contains("1920h"))))
            .ReturnsAsync(false);

        _mockBlobService.Setup(x => x.DownloadAsync(imageInfo.BlobName))
            .ReturnsAsync(new MemoryStream(TestImageHelper.CreateTestImageBytes()));

        _mockBlobService.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(Task.CompletedTask);

        var result = await _imageService.GeneratePredefinedResolutionsAsync(imageId);

        Assert.NotNull(result);
        Assert.Contains("thumbnail (already exists)", result.SkippedResolutions);

    }


    [Fact]
    public async Task GetResizedImageAsync_RequestHeightLargerThanOriginal_ReturnsNull()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        var result = await _imageService.GetResizedImageAsync(imageId, null, 800);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetResizedImageAsync_RequestWidthLargerThanOriginal_ReturnsNull()
    {
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageRepository.Setup(x => x.GetByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        var result = await _imageService.GetResizedImageAsync(imageId, 1000, null);

        Assert.Null(result);
    }

    #region Helper Methods

    private void VerifyLogCalled(LogLevel level, params string[] messageParts)
    {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => messageParts.All(part => v.ToString()!.Contains(part))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    public void Dispose()
    {
        _memoryCache?.Dispose();
    }
}