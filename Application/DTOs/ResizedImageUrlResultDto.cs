namespace ImageApi.Application.DTOs;

public class ResizedImageUrlResultDto
{
    public string? ImageId { get; set; }
    public int Height { get; set; }
    public string? Url { get; set; }
    public string? Path { get; set; }
    public string? Error { get; set; }
}