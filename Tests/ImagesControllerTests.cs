using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using ImageApi.Domain.Entities;
using ImageApi.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ImageApi.Tests.Controllers;

public class ImagesControllerTests
{
    private readonly Mock<IImageService> _mockImageService;
    private readonly ImagesController _controller;

    public ImagesControllerTests()
    {
        _mockImageService = new Mock<IImageService>();
        _controller = new ImagesController(_mockImageService.Object);
    }

    #region Upload Tests

    [Fact]
    public async Task UploadImage_ValidFile_ReturnsOkWithResult()
    {
        // Arrange
        var testFile = TestImageHelper.CreateTestImageFile("test.png", 800, 600);
        var expectedResult = new ImageUploadResultDto { Id = "test-id", Path = "original/test-id_original.png" };

        _mockImageService.Setup(x => x.UploadImageAsync(testFile))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.UploadImage(testFile);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualResult = Assert.IsType<ImageUploadResultDto>(okResult.Value);
        Assert.Equal(expectedResult.Id, actualResult.Id);
        Assert.Equal(expectedResult.Path, actualResult.Path);

        _mockImageService.Verify(x => x.UploadImageAsync(testFile), Times.Once);
    }

    [Fact]
    public async Task UploadImage_NullFile_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.UploadImage(null);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("File is empty.", badRequestResult.Value);

        _mockImageService.Verify(x => x.UploadImageAsync(It.IsAny<IFormFile>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        var emptyFile = new MockFormFile(Array.Empty<byte>(), "empty.png");

        // Act
        var result = await _controller.UploadImage(emptyFile);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("File is empty.", badRequestResult.Value);

        _mockImageService.Verify(x => x.UploadImageAsync(It.IsAny<IFormFile>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_ServiceThrowsException_PropagatesException()
    {
        // Arrange
        var testFile = TestImageHelper.CreateTestImageFile("test.png", 800, 600);
        _mockImageService.Setup(x => x.UploadImageAsync(testFile))
            .ThrowsAsync(new InvalidOperationException("Upload failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.UploadImage(testFile));
        Assert.Equal("Upload failed", exception.Message);
    }

    #endregion

    #region GetAllImages Tests

    [Fact]
    public async Task GetAllImages_ReturnsOkWithImageList()
    {
        // Arrange
        var expectedImages = new List<ImageInfo>
        {
            new() { Id = "1", OriginalFileName = "image1.png", OriginalWidth = 800, OriginalHeight = 600 },
            new() { Id = "2", OriginalFileName = "image2.jpg", OriginalWidth = 1024, OriginalHeight = 768 }
        };

        _mockImageService.Setup(x => x.GetAllImagesAsync())
            .ReturnsAsync(expectedImages);

        // Act
        var result = await _controller.GetAllImages();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualImages = Assert.IsAssignableFrom<IEnumerable<ImageInfo>>(okResult.Value);
        Assert.Equal(2, actualImages.Count());

        _mockImageService.Verify(x => x.GetAllImagesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAllImages_EmptyList_ReturnsOkWithEmptyList()
    {
        // Arrange
        _mockImageService.Setup(x => x.GetAllImagesAsync())
            .ReturnsAsync(new List<ImageInfo>());

        // Act
        var result = await _controller.GetAllImages();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualImages = Assert.IsAssignableFrom<IEnumerable<ImageInfo>>(okResult.Value);
        Assert.Empty(actualImages);
    }

    #endregion

    #region GetImageById Tests

    [Fact]
    public async Task GetImageById_ExistingImage_ReturnsOkWithImage()
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

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(expectedImage);

        // Act
        var result = await _controller.GetImageById(imageId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualImage = Assert.IsType<ImageInfo>(okResult.Value);
        Assert.Equal(expectedImage.Id, actualImage.Id);
        Assert.Equal(expectedImage.OriginalFileName, actualImage.OriginalFileName);

        _mockImageService.Verify(x => x.GetImageByIdAsync(imageId), Times.Once);
    }

    [Fact]
    public async Task GetImageById_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";
        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _controller.GetImageById(imageId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region GetResizedImageByHeight Tests

    [Fact]
    public async Task GetResizedImageByHeight_ValidRequest_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var height = 300;
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalHeight = 600,
            OriginalWidth = 800
        };
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test_300h.png"
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, null, height))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.GetResizedImageByHeight(imageId, height);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.GetImageByIdAsync(imageId), Times.Once);
        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, null, height), Times.Once);
    }

    [Fact]
    public async Task GetResizedImageByHeight_ImageNotFound_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";
        var height = 300;

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _controller.GetResizedImageByHeight(imageId, height);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    [Fact]
    public async Task GetResizedImageByHeight_HeightExceedsOriginal_ReturnsBadRequest()
    {
        // Arrange
        var imageId = "test-id";
        var height = 800;
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalHeight = 600,
            OriginalWidth = 800
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act
        var result = await _controller.GetResizedImageByHeight(imageId, height);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Requested height cannot be greater than original image height.", badRequestResult.Value);
    }

    [Fact]
    public async Task GetResizedImageByHeight_ResizeServiceReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var imageId = "test-id";
        var height = 300;
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalHeight = 600,
            OriginalWidth = 800
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, null, height))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.GetResizedImageByHeight(imageId, height);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Could not generate resized image.", notFoundResult.Value);
    }

    #endregion

    #region GetResizedImageUrl Tests

    [Fact]
    public async Task GetResizedImageUrl_ValidRequest_ReturnsOkWithUrl()
    {
        // Arrange
        var imageId = "test-id";
        var height = 300;
        var expectedResult = new ResizedImageUrlResultDto
        {
            ImageId = imageId,
            Height = height,
            Url = $"/api/images/{imageId}/resize?height={height}",
            Path = $"resized/{imageId}_{height}h.png",
            Error = null
        };

        _mockImageService.Setup(x => x.GetResizedImageUrlAsync(imageId, height))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GetResizedImageUrl(imageId, height);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualResult = Assert.IsType<ResizedImageUrlResultDto>(okResult.Value);
        Assert.Equal(expectedResult.ImageId, actualResult.ImageId);
        Assert.Equal(expectedResult.Url, actualResult.Url);
        Assert.Null(actualResult.Error);
    }

    [Fact]
    public async Task GetResizedImageUrl_ServiceReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";
        var height = 300;

        _mockImageService.Setup(x => x.GetResizedImageUrlAsync(imageId, height))
            .ReturnsAsync((ResizedImageUrlResultDto?)null);

        // Act
        var result = await _controller.GetResizedImageUrl(imageId, height);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    [Fact]
    public async Task GetResizedImageUrl_ServiceReturnsError_ReturnsBadRequest()
    {
        // Arrange
        var imageId = "test-id";
        var height = 300;
        var resultWithError = new ResizedImageUrlResultDto
        {
            Error = "Requested height cannot be greater than original image height."
        };

        _mockImageService.Setup(x => x.GetResizedImageUrlAsync(imageId, height))
            .ReturnsAsync(resultWithError);

        // Act
        var result = await _controller.GetResizedImageUrl(imageId, height);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(resultWithError.Error, badRequestResult.Value);
    }

    #endregion

    #region DownloadImage Tests

    [Fact]
    public async Task DownloadImage_ExistingImage_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test.png"
        };

        _mockImageService.Setup(x => x.DownloadImageAsync(imageId))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.DownloadImage(imageId);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);
    }

    [Fact]
    public async Task DownloadImage_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";

        _mockImageService.Setup(x => x.DownloadImageAsync(imageId))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.DownloadImage(imageId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region DownloadImageWithResolution Tests

    [Fact]
    public async Task DownloadImageWithResolution_ValidRequest_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var resolution = "thumbnail";
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test_thumbnail.png"
        };

        _mockImageService.Setup(x => x.DownloadImageWithResolutionAsync(imageId, resolution))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.DownloadImageWithResolution(imageId, resolution);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.DownloadImageWithResolutionAsync(imageId, resolution), Times.Once);
    }

    [Fact]
    public async Task DownloadImageWithResolution_InvalidResolution_ReturnsNotFound()
    {
        // Arrange
        var imageId = "test-id";
        var resolution = "invalid-resolution";

        _mockImageService.Setup(x => x.DownloadImageWithResolutionAsync(imageId, resolution))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.DownloadImageWithResolution(imageId, resolution);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found or resolution {resolution} is invalid.", notFoundResult.Value);
    }

    #endregion

    #region GetResizedImage Tests

    [Fact]
    public async Task GetResizedImage_WithWidth_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var width = 400;
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test_400w.png"
        };

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, width, null))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.GetResizedImage(imageId, width, null);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, width, null), Times.Once);
    }

    [Fact]
    public async Task GetResizedImage_WithHeight_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var height = 300;
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test_300h.png"
        };

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, null, height))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.GetResizedImage(imageId, null, height);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, null, height), Times.Once);
    }

    [Fact]
    public async Task GetResizedImage_WithBothDimensions_ReturnsFileResult()
    {
        // Arrange
        var imageId = "test-id";
        var width = 400;
        var height = 300;
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = "test_400w.png"
        };

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, width, height))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.GetResizedImage(imageId, width, height);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, width, height), Times.Once);
    }

    [Fact]
    public async Task GetResizedImage_NoDimensions_ReturnsBadRequest()
    {
        // Arrange
        var imageId = "test-id";

        // Act
        var result = await _controller.GetResizedImage(imageId, null, null);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Either width or height must be specified.", badRequestResult.Value);

        _mockImageService.Verify(x => x.GetResizedImageAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GetResizedImage_ServiceReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var imageId = "test-id";
        var width = 400;

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, width, null))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.GetResizedImage(imageId, width, null);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found or invalid dimensions.", notFoundResult.Value);
    }

    #endregion

    #region GetAvailableResolutions Tests

    [Fact]
    public async Task GetAvailableResolutions_ExistingImage_ReturnsOkWithResolutions()
    {
        // Arrange
        var imageId = "test-id";
        var expectedResolutions = new List<string> { "original", "thumbnail", "small", "medium", "large" };

        _mockImageService.Setup(x => x.GetAvailableResolutionsAsync(imageId))
            .ReturnsAsync(expectedResolutions);

        // Act
        var result = await _controller.GetAvailableResolutions(imageId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualResolutions = Assert.IsAssignableFrom<IEnumerable<string>>(okResult.Value);
        Assert.Equal(expectedResolutions.Count, actualResolutions.Count());
        Assert.Contains("original", actualResolutions);
        Assert.Contains("thumbnail", actualResolutions);

        _mockImageService.Verify(x => x.GetAvailableResolutionsAsync(imageId), Times.Once);
    }

    [Fact]
    public async Task GetAvailableResolutions_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";

        _mockImageService.Setup(x => x.GetAvailableResolutionsAsync(imageId))
            .ReturnsAsync((IEnumerable<string>?)null);

        // Act
        var result = await _controller.GetAvailableResolutions(imageId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region UpdateImage Tests

    [Fact]
    public async Task UpdateImage_ValidFile_ReturnsOkWithResult()
    {
        // Arrange
        var imageId = "test-id";
        var updateFile = TestImageHelper.CreateTestImageFile("updated.png", 1000, 750);
        var expectedResult = new ImageUploadResultDto { Id = imageId, Path = "original/test-id.png" };

        _mockImageService.Setup(x => x.UpdateImageAsync(imageId, updateFile))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.UpdateImage(imageId, updateFile);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualResult = Assert.IsType<ImageUploadResultDto>(okResult.Value);
        Assert.Equal(expectedResult.Id, actualResult.Id);
        Assert.Equal(expectedResult.Path, actualResult.Path);

        _mockImageService.Verify(x => x.UpdateImageAsync(imageId, updateFile), Times.Once);
    }

    [Fact]
    public async Task UpdateImage_NullFile_ReturnsBadRequest()
    {
        // Arrange
        var imageId = "test-id";

        // Act
        var result = await _controller.UpdateImage(imageId, null);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("File is empty.", badRequestResult.Value);

        _mockImageService.Verify(x => x.UpdateImageAsync(It.IsAny<string>(), It.IsAny<IFormFile>()), Times.Never);
    }

    [Fact]
    public async Task UpdateImage_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        var imageId = "test-id";
        var emptyFile = new MockFormFile(Array.Empty<byte>(), "empty.png");

        // Act
        var result = await _controller.UpdateImage(imageId, emptyFile);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("File is empty.", badRequestResult.Value);

        _mockImageService.Verify(x => x.UpdateImageAsync(It.IsAny<string>(), It.IsAny<IFormFile>()), Times.Never);
    }

    [Fact]
    public async Task UpdateImage_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";
        var updateFile = TestImageHelper.CreateTestImageFile("updated.png", 1000, 750);

        _mockImageService.Setup(x => x.UpdateImageAsync(imageId, updateFile))
            .ReturnsAsync((ImageUploadResultDto?)null);

        // Act
        var result = await _controller.UpdateImage(imageId, updateFile);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region DeleteImage Tests

    [Fact]
    public async Task DeleteImage_ExistingImage_ReturnsNoContent()
    {
        // Arrange
        var imageId = "test-id";

        _mockImageService.Setup(x => x.DeleteImageAsync(imageId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteImage(imageId);

        // Assert
        Assert.IsType<NoContentResult>(result);

        _mockImageService.Verify(x => x.DeleteImageAsync(imageId), Times.Once);
    }

    [Fact]
    public async Task DeleteImage_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";

        _mockImageService.Setup(x => x.DeleteImageAsync(imageId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.DeleteImage(imageId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region GeneratePredefinedResolutions Tests

    [Fact]
    public async Task GeneratePredefinedResolutions_ExistingImage_ReturnsOkWithResult()
    {
        // Arrange
        var imageId = "test-id";
        var expectedResult = new ResolutionGenerationResultDto
        {
            ImageId = imageId,
            GeneratedResolutions = new List<string> { "thumbnail", "small", "medium" },
            SkippedResolutions = new List<string> { "large (already exists)", "xlarge (exceeds original dimensions)" }
        };

        _mockImageService.Setup(x => x.GeneratePredefinedResolutionsAsync(imageId))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GeneratePredefinedResolutions(imageId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var actualResult = Assert.IsType<ResolutionGenerationResultDto>(okResult.Value);
        Assert.Equal(expectedResult.ImageId, actualResult.ImageId);
        Assert.Equal(expectedResult.GeneratedResolutions.Count, actualResult.GeneratedResolutions.Count);
        Assert.Equal(expectedResult.SkippedResolutions.Count, actualResult.SkippedResolutions.Count);

        _mockImageService.Verify(x => x.GeneratePredefinedResolutionsAsync(imageId), Times.Once);
    }

    [Fact]
    public async Task GeneratePredefinedResolutions_NonExistingImage_ReturnsNotFound()
    {
        // Arrange
        var imageId = "non-existing-id";

        _mockImageService.Setup(x => x.GeneratePredefinedResolutionsAsync(imageId))
            .ReturnsAsync((ResolutionGenerationResultDto?)null);

        // Act
        var result = await _controller.GeneratePredefinedResolutions(imageId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found.", notFoundResult.Value);
    }

    #endregion

    #region Integration-style Tests

    [Fact]
    public async Task UploadAndRetrieveWorkflow_Success()
    {
        // Arrange
        var testFile = TestImageHelper.CreateTestImageFile("workflow-test.png", 800, 600);
        var uploadResult = new ImageUploadResultDto { Id = "workflow-id", Path = "original/workflow-id_original.png" };
        var imageInfo = new ImageInfo
        {
            Id = "workflow-id",
            OriginalFileName = "workflow-test.png",
            OriginalWidth = 800,
            OriginalHeight = 600
        };

        _mockImageService.Setup(x => x.UploadImageAsync(testFile))
            .ReturnsAsync(uploadResult);
        _mockImageService.Setup(x => x.GetImageByIdAsync("workflow-id"))
            .ReturnsAsync(imageInfo);

        // Act - Upload
        var uploadResponse = await _controller.UploadImage(testFile);
        var uploadOkResult = Assert.IsType<OkObjectResult>(uploadResponse);
        var actualUploadResult = Assert.IsType<ImageUploadResultDto>(uploadOkResult.Value);

        // Act - Retrieve
        var retrieveResponse = await _controller.GetImageById(actualUploadResult.Id);
        var retrieveOkResult = Assert.IsType<OkObjectResult>(retrieveResponse);
        var actualImageInfo = Assert.IsType<ImageInfo>(retrieveOkResult.Value);

        // Assert
        Assert.Equal(uploadResult.Id, actualUploadResult.Id);
        Assert.Equal(imageInfo.Id, actualImageInfo.Id);
        Assert.Equal(imageInfo.OriginalFileName, actualImageInfo.OriginalFileName);
        // Verify service calls
        _mockImageService.Verify(x => x.UploadImageAsync(testFile), Times.Once);
        _mockImageService.Verify(x => x.GetImageByIdAsync("workflow-id"), Times.Once);
    }

    [Fact]
    public async Task ResizeWorkflow_MultipleFormats_Success()
    {
        // Arrange
        var imageId = "resize-test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 1000,
            OriginalHeight = 800
        };

        var resizeByHeightResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes(500, 400)),
            ContentType = "image/png",
            FileName = "test_400h.png"
        };

        var resizeByWidthResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes(600, 480)),
            ContentType = "image/png",
            FileName = "test_600w.png"
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);
        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, null, 400))
            .ReturnsAsync(resizeByHeightResult);
        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, 600, null))
            .ReturnsAsync(resizeByWidthResult);

        // Act & Assert - Resize by height
        var heightResult = await _controller.GetResizedImageByHeight(imageId, 400);
        var heightFileResult = Assert.IsType<FileStreamResult>(heightResult);
        Assert.Equal("image/png", heightFileResult.ContentType);
        Assert.Equal("test_400h.png", heightFileResult.FileDownloadName);

        // Act & Assert - Resize by width
        var widthResult = await _controller.GetResizedImage(imageId, 600, null);
        var widthFileResult = Assert.IsType<FileStreamResult>(widthResult);
        Assert.Equal("image/png", widthFileResult.ContentType);
        Assert.Equal("test_600w.png", widthFileResult.FileDownloadName);

        // Verify service calls
        _mockImageService.Verify(x => x.GetImageByIdAsync(imageId), Times.Once);
        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, null, 400), Times.Once);
        _mockImageService.Verify(x => x.GetResizedImageAsync(imageId, 600, null), Times.Once);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetImageById_InvalidId_ReturnsNotFound(string invalidId)
    {
        // Arrange
        _mockImageService.Setup(x => x.GetImageByIdAsync(invalidId))
            .ReturnsAsync((ImageInfo?)null);

        // Act
        var result = await _controller.GetImageById(invalidId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("not found", notFoundResult.Value?.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetResizedImageByHeight_InvalidHeight_ServiceHandlesValidation(int invalidHeight)
    {
        // Arrange
        var imageId = "test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalHeight = 600,
            OriginalWidth = 800
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Note: The controller doesn't validate negative heights, 
        // but the service should handle this gracefully
        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, null, invalidHeight))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.GetResizedImageByHeight(imageId, invalidHeight);

        // Assert
        // For negative/zero heights, the service should return null
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Could not generate resized image.", notFoundResult.Value);
    }

    [Fact]
    public async Task GetResizedImage_ExtremelyLargeDimensions_ServiceHandlesGracefully()
    {
        // Arrange
        var imageId = "test-id";
        var extremeWidth = int.MaxValue;
        var extremeHeight = int.MaxValue;

        _mockImageService.Setup(x => x.GetResizedImageAsync(imageId, extremeWidth, extremeHeight))
            .ReturnsAsync((ImageDownloadResultDto?)null);

        // Act
        var result = await _controller.GetResizedImage(imageId, extremeWidth, extremeHeight);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Image with ID {imageId} not found or invalid dimensions.", notFoundResult.Value);
    }

    [Fact]
    public async Task DownloadImage_ServiceThrowsException_PropagatesException()
    {
        // Arrange
        var imageId = "test-id";
        _mockImageService.Setup(x => x.DownloadImageAsync(imageId))
            .ThrowsAsync(new InvalidOperationException("Storage service unavailable"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.DownloadImage(imageId));
        Assert.Equal("Storage service unavailable", exception.Message);
    }

    [Fact]
    public async Task UpdateImage_ServiceThrowsException_PropagatesException()
    {
        // Arrange
        var imageId = "test-id";
        var updateFile = TestImageHelper.CreateTestImageFile("update.png", 800, 600);

        _mockImageService.Setup(x => x.UpdateImageAsync(imageId, updateFile))
            .ThrowsAsync(new InvalidOperationException("Update failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.UpdateImage(imageId, updateFile));
        Assert.Equal("Update failed", exception.Message);
    }

    #endregion

    #region Performance and Concurrency Tests

    [Fact]
    public async Task MultipleSimultaneousRequests_HandledCorrectly()
    {
        // Arrange
        var imageId = "concurrent-test-id";
        var imageInfo = new ImageInfo
        {
            Id = imageId,
            OriginalWidth = 1000,
            OriginalHeight = 800
        };

        _mockImageService.Setup(x => x.GetImageByIdAsync(imageId))
            .ReturnsAsync(imageInfo);

        // Act - Simulate multiple concurrent requests
        var tasks = new List<Task<IActionResult>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_controller.GetImageById(imageId));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result =>
        {
            var okResult = Assert.IsType<OkObjectResult>(result);
            var actualImage = Assert.IsType<ImageInfo>(okResult.Value);
            Assert.Equal(imageId, actualImage.Id);
        });

        // Verify service was called for each request
        _mockImageService.Verify(x => x.GetImageByIdAsync(imageId), Times.Exactly(10));
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("original")]
    [InlineData("thumbnail")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("xlarge")]
    public async Task DownloadImageWithResolution_ValidResolutions_CallsService(string resolution)
    {
        // Arrange
        var imageId = "test-id";
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = new MemoryStream(TestImageHelper.CreateTestImageBytes()),
            ContentType = "image/png",
            FileName = $"test_{resolution}.png"
        };

        _mockImageService.Setup(x => x.DownloadImageWithResolutionAsync(imageId, resolution))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.DownloadImageWithResolution(imageId, resolution);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", fileResult.ContentType);
        Assert.Equal($"test_{resolution}.png", fileResult.FileDownloadName);

        _mockImageService.Verify(x => x.DownloadImageWithResolutionAsync(imageId, resolution), Times.Once);
    }

    #endregion

    #region Cleanup and Resource Management Tests

    [Fact]
    public async Task FileStreamResults_ProperlyConfigured()
    {
        // Arrange
        var imageId = "test-id";
        var testStream = new MemoryStream(TestImageHelper.CreateTestImageBytes());
        var downloadResult = new ImageDownloadResultDto
        {
            Stream = testStream,
            ContentType = "image/jpeg",
            FileName = "test-image.jpg"
        };

        _mockImageService.Setup(x => x.DownloadImageAsync(imageId))
            .ReturnsAsync(downloadResult);

        // Act
        var result = await _controller.DownloadImage(imageId);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(downloadResult.Stream, fileResult.FileStream);
        Assert.Equal(downloadResult.ContentType, fileResult.ContentType);
        Assert.Equal(downloadResult.FileName, fileResult.FileDownloadName);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ValidImageService_CreatesInstance()
    {
        // Arrange & Act
        var controller = new ImagesController(_mockImageService.Object);

        // Assert
        Assert.NotNull(controller);
    }

    #endregion
}