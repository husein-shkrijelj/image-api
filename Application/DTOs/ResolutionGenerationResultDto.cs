namespace ImageApi.Application.DTOs
{
    public class ResolutionGenerationResultDto
    {
        public string ImageId { get; set; } = string.Empty;
        public List<string> GeneratedResolutions { get; set; } = new();
        public List<string> SkippedResolutions { get; set; } = new();
    }
}