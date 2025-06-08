using ImageApi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _imageService;

    public ImagesController(IImageService imageService)
    {
        _imageService = imageService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        var result = await _imageService.UploadImageAsync(file);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllImages()
    {
        var images = await _imageService.GetAllImagesAsync();
        return Ok(images);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetImageById(string id)
    {
        var image = await _imageService.GetImageByIdAsync(id);
        if (image == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(image);
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadImage(string id)
    {
        var result = await _imageService.DownloadImageAsync(id);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    [HttpGet("{id}/download/{resolution}")]
    public async Task<IActionResult> DownloadImageWithResolution(string id, string resolution)
    {
        var result = await _imageService.DownloadImageWithResolutionAsync(id, resolution);
        if (result == null)
            return NotFound($"Image with ID {id} not found or resolution {resolution} is invalid.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    [HttpGet("{id}/resize")]
    public async Task<IActionResult> GetResizedImage(string id, [FromQuery] int? width = null, [FromQuery] int? height = null)
    {
        if (width == null && height == null)
            return BadRequest("Either width or height must be specified.");

        var result = await _imageService.GetResizedImageAsync(id, width, height);
        if (result == null)
            return NotFound($"Image with ID {id} not found or invalid dimensions.");

        return File(result.Stream, result.ContentType, result.FileName);
    }

    [HttpGet("{id}/resolutions")]
    public async Task<IActionResult> GetAvailableResolutions(string id)
    {
        var resolutions = await _imageService.GetAvailableResolutionsAsync(id);
        if (resolutions == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(resolutions);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateImage(string id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        var result = await _imageService.UpdateImageAsync(id, file);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteImage(string id)
    {
        var success = await _imageService.DeleteImageAsync(id);
        if (!success)
            return NotFound($"Image with ID {id} not found.");

        return NoContent();
    }

    [HttpPost("{id}/generate-resolutions")]
    public async Task<IActionResult> GeneratePredefinedResolutions(string id)
    {
        var result = await _imageService.GeneratePredefinedResolutionsAsync(id);
        if (result == null)
            return NotFound($"Image with ID {id} not found.");

        return Ok(result);
    }
}