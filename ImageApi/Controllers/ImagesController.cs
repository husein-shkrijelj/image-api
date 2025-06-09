using ImageApi.Application.DTOs;
using ImageApi.Application.Interfaces;
using ImageApi.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Controller for managing image operations including upload, download, resize, and CRUD operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _imageService;

    public ImagesController(IImageService imageService)
    {
        _imageService = imageService;
    }

    /// <summary>
    /// Uploads a new image to the system.
    /// </summary>
    /// <param name="file">The image file to upload. Supported formats: JPG, PNG, GIF, BMP, WebP, TIFF.</param>
    /// <returns>Returns the uploaded image ID and storage path.</returns>
    /// <response code="200">Image uploaded successfully.</response>
    /// <response code="400">Invalid file or file is empty.</response>
    /// <response code="500">Internal server error during upload.</response>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(ImageUploadResultDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        var result = await _imageService.UploadImageAsync(file);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all images in the system.
    /// </summary>
    /// <returns>A list of all uploaded images with their metadata.</returns>
    /// <response code="200">Successfully retrieved all images.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ImageInfo>), 200)]
    public async Task<IActionResult> GetAllImages()
    {
        var images = await _imageService.GetAllImagesAsync();
        return Ok(images);
    }

    /// <summary>
    /// Retrieves metadata for a specific image by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <returns>Image metadata including dimensions, file size, and upload date.</returns>
    /// <response code="200">Image metadata retrieved successfully.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ImageInfo), 200)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> GetImageById(string id)
    {
        var image = await _imageService.GetImageByIdAsync(id);
        if (image == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(image);
    }

    /// <summary>
    /// Downloads a resized version of an image with the specified height.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <param name="height">The desired height in pixels. Width will be calculated to maintain aspect ratio.</param>
    /// <returns>The resized image file.</returns>
    /// <response code="200">Resized image file.</response>
    /// <response code="400">Requested height exceeds original image height.</response>
    /// <response code="404">Image not found or could not generate resized version.</response>
    [HttpGet("{id}/resize/{height:int}")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    [Produces("image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp")]
    public async Task<IActionResult> GetResizedImageByHeight(string id, int height)
    {
        var image = await _imageService.GetImageByIdAsync(id);
        if (image == null)
            return NotFound($"Image with ID {id} not found.");

        if (height > image.OriginalHeight)
            return BadRequest("Requested height cannot be greater than original image height.");

        var result = await _imageService.GetResizedImageAsync(id, null, height);
        if (result == null)
            return NotFound($"Could not generate resized image.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    /// <summary>
    /// Gets a URL for downloading a resized version of an image with the specified height.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <param name="height">The desired height in pixels. Width will be calculated to maintain aspect ratio.</param>
    /// <returns>A URL that can be used to download the resized image.</returns>
    /// <response code="200">URL generated successfully.</response>
    /// <response code="400">Requested height exceeds original image height or other validation error.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    [HttpGet("{id}/resize/{height:int}/url")]
    [ProducesResponseType(typeof(ResizedImageUrlResultDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> GetResizedImageUrl(string id, int height)
    {
        var result = await _imageService.GetResizedImageUrlAsync(id, height);

        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        if (result.Error != null)
            return BadRequest(result.Error);

        return Ok(result);
    }

    /// <summary>
    /// Downloads the original image file.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <returns>The original image file.</returns>
    /// <response code="200">Original image file.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    [HttpGet("{id}/download")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(string), 404)]
    [Produces("image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp")]
    public async Task<IActionResult> DownloadImage(string id)
    {
        var result = await _imageService.DownloadImageAsync(id);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    /// <summary>
    /// Downloads an image in a predefined resolution (thumbnail, small, medium, large, xlarge).
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <param name="resolution">The predefined resolution name. Valid values: original, thumbnail (160px), small (320px), medium (640px), large (1024px), xlarge (1920px).</param>
    /// <returns>The image file in the requested resolution.</returns>
    /// <response code="200">Image file in the requested resolution.</response>
    /// <response code="404">Image not found or invalid resolution specified.</response>
    [HttpGet("{id}/download/{resolution}")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(string), 404)]
    [Produces("image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp")]
    public async Task<IActionResult> DownloadImageWithResolution(string id, string resolution)
    {
        var result = await _imageService.DownloadImageWithResolutionAsync(id, resolution);
        if (result == null)
            return NotFound($"Image with ID {id} not found or resolution {resolution} is invalid.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    /// <summary>
    /// Downloads a resized version of an image with custom width and/or height.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <param name="width">The desired width in pixels (optional). If specified, height will be calculated to maintain aspect ratio.</param>
    /// <param name="height">The desired height in pixels (optional). If specified, width will be calculated to maintain aspect ratio.</param>
    /// <returns>The resized image file.</returns>
    /// <response code="200">Resized image file.</response>
    /// <response code="400">Either width or height must be specified, or dimensions exceed original image size.</response>
    /// <response code="404">Image not found or could not generate resized version.</response>
    /// <remarks>
    /// You must specify either width OR height, not both. The other dimension will be calculated automatically to maintain the original aspect ratio.
    /// 
    /// Examples:
    /// - GET /api/images/abc123/resize?height=300 - Resize to 300px height, width calculated automatically
    /// - GET /api/images/abc123/resize?width=500 - Resize to 500px width, height calculated automatically
    /// </remarks>
    [HttpGet("{id}/resize")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    [Produces("image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp")]
    public async Task<IActionResult> GetResizedImage(string id, [FromQuery] int? width = null, [FromQuery] int? height = null)
    {
        if (width == null && height == null)
            return BadRequest("Either width or height must be specified.");

        var result = await _imageService.GetResizedImageAsync(id, width, height);
        if (result == null)
            return NotFound($"Image with ID {id} not found or invalid dimensions.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    /// <summary>
    /// Gets a list of available predefined resolutions for a specific image.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <returns>A list of resolution names that can be generated for this image.</returns>
    /// <response code="200">List of available resolutions.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    /// <remarks>
    /// Returns resolution names like: original, thumbnail, small, medium, large, xlarge.
    /// Some resolutions may not be available if they exceed the original image dimensions.
    /// </remarks>
    [HttpGet("{id}/resolutions")]
    [ProducesResponseType(typeof(IEnumerable<string>), 200)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> GetAvailableResolutions(string id)
    {
        var resolutions = await _imageService.GetAvailableResolutionsAsync(id);
        if (resolutions == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(resolutions);
    }

    /// <summary>
    /// Updates an existing image by replacing it with a new file.
    /// </summary>
    /// <param name="id">The unique identifier of the image to update.</param>
    /// <param name="file">The new image file to replace the existing one.</param>
    /// <returns>Updated image information.</returns>
    /// <response code="200">Image updated successfully.</response>
    /// <response code="400">Invalid file or file is empty.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    /// <remarks>
    /// This operation will:
    /// - Replace the original image with the new file
    /// - Delete all existing resized versions
    /// - Update the image metadata (dimensions, file size, etc.)
    /// - Clear related cache entries
    /// </remarks>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ImageUploadResultDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> UpdateImage(string id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        var result = await _imageService.UpdateImageAsync(id, file);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(result);
    }

    /// <summary>
    /// Permanently deletes an image and all its resized versions.
    /// </summary>
    /// <param name="id">The unique identifier of the image to delete.</param>
    /// <returns>No content on successful deletion.</returns>
    /// <response code="204">Image deleted successfully.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    /// <remarks>
    /// This operation will:
    /// - Delete the original image file
    /// - Delete all resized versions (thumbnails, variations, etc.)
    /// - Remove the image record from the database
    /// - Clear all related cache entries
    /// 
    /// This action cannot be undone.
    /// </remarks>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> DeleteImage(string id)
    {
        var success = await _imageService.DeleteImageAsync(id);
        if (!success)
            return NotFound($"Image with ID {id} not found.");

        return NoContent();
    }

    /// <summary>
    /// Pre-generates all predefined resolution variations for an image.
    /// </summary>
    /// <param name="id">The unique identifier of the image.</param>
    /// <returns>A summary of which resolutions were generated and which were skipped.</returns>
    /// <response code="200">Resolution generation completed with summary.</response>
    /// <response code="404">Image with the specified ID was not found.</response>
    /// <remarks>
    /// This endpoint will attempt to generate all predefined resolutions:
    /// - thumbnail (160px height)
    /// - small (320px height)
    /// - medium (640px height)
    /// - large (1024px height)
    /// - xlarge (1920px height)
    /// 
    /// Resolutions that exceed the original image dimensions will be skipped.
    /// Already existing resolutions will also be skipped.
    /// 
    /// This is useful for pre-generating commonly used sizes to improve response times.
    /// </remarks>
    [HttpPost("{id}/generate-resolutions")]
    [ProducesResponseType(typeof(ResolutionGenerationResultDto), 200)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<IActionResult> GeneratePredefinedResolutions(string id)
    {
        var result = await _imageService.GeneratePredefinedResolutionsAsync(id);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(result);
    }
}
