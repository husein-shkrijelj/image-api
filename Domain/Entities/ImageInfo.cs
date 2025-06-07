namespace ImageApi.Domain.Entities
{
    public class ImageInfo
    {
        public string Id { get; set; } = string.Empty;
        public string BlobName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public int OriginalHeight { get; set; }
        public int OriginalWidth { get; set; }
        public long Size { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
