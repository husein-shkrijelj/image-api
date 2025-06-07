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

    [HttpGet("{id}/resize")]
    public async Task<IActionResult> GetResizedImage(string id, [FromQuery] int height)
    {
        var result = await _imageService.GetResizedImagePathAsync(id, height);
        return result != null ? Ok(result) : BadRequest("Invalid height or image not found.");
    }

    [HttpGet]
    public async Task<IActionResult> GetAllImages()
    {
        var images = await _imageService.GetAllImagesAsync();
        return Ok(images);
    }
}
