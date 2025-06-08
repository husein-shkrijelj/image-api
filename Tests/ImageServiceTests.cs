using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using ImageApi.Application.Services;
using ImageApi.Domain.Entities;
using ImageApi.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace ImageApi.Tests.Services;

public class ImageServiceTests : IDisposable
{
    private readonly Mock<IAzureBlobStorageService> _mockBlobService;
    private readonly Mock<IImageRepository> _mockImageRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly ImageService _imageService;

    public ImageServiceTests()
    {
        _mockBlobService = new Mock<IAzureBlobStorageService>();
        _mockImageRepository = new Mock<IImageRepository>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _imageService = new ImageService(_mockBlobService.Object, _mockImageRepository.Object, _memoryCache);
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

        _mockBlobService.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        _mockImageRepository.Verify(x => x.AddAsync(It.IsAny<ImageInfo>()), Times.Once);
    }

    [Fact]
    public async Task UploadImageAsync_NullFile_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _imageService.UploadImageAsync(null));
        Assert.Contains("File is empty or null", exception.Message);
    }

    [Fact]
    public async Task UploadImageAsync_EmptyFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyFile = new MockFormFile(Array.Empty<byte>(), "empty.png");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _imageService.UploadImageAsync(emptyFile));
        Assert.Contains("File is empty or null", exception.Message);
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
    }

    [Fact]
    public async Task DownloadImageAsync_ExistingImage_ReturnsDownloadResult()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.dat",
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
            BlobName = "original/test_original.dat"
        };

        var testStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());
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
    }

    [Fact]
    public async Task DeleteImageAsync_ExistingImage_ReturnsTrue()
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            BlobName = "original/test_original.dat"
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
    }

    public void Dispose()
    {
        _memoryCache?.Dispose();
    }
}
