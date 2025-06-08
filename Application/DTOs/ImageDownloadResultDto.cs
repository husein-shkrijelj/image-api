namespace ImageApi.Application.DTOs
{
    public class ImageDownloadResultDto
    {
        public Stream Stream { get; set; } = null!;
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}