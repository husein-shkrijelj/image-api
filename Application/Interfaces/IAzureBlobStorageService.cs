namespace ImageApi.Application.Interfaces
{
    public interface IAzureBlobStorageService
    {
        Task UploadAsync(string blobName, Stream content);
        Task<Stream> DownloadAsync(string blobName);
        Task<bool> ExistsAsync(string blobName);
    }
}
