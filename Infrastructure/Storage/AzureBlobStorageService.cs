using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ImageApi.Application.Interfaces;
using ImageApi.Infrastructure.Config;
using Microsoft.Extensions.Options;

namespace ImageApi.Infrastructure.Storage;

public class AzureBlobStorageService : IAzureBlobStorageService
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(IOptions<AzureBlobSettings> options)
    {
        var settings = options.Value;
        var serviceClient = new BlobServiceClient(settings.ConnectionString);
        _containerClient = serviceClient.GetBlobContainerClient(settings.ContainerName);

        _containerClient.CreateIfNotExists();
    }

    public async Task UploadAsync(string blobName, Stream content)
    {
        var blob = _containerClient.GetBlobClient(blobName);
        await blob.UploadAsync(content, overwrite: true);
    }

    public async Task<Stream> DownloadAsync(string blobName)
    {
        var blob = _containerClient.GetBlobClient(blobName);
        var response = await blob.DownloadAsync();
        var memoryStream = new MemoryStream();
        await response.Value.Content.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<bool> ExistsAsync(string blobName)
    {
        var blob = _containerClient.GetBlobClient(blobName);
        return await blob.ExistsAsync();
    }
}
