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
        try
        {
            var blob = _containerClient.GetBlobClient(blobName);
            await blob.UploadAsync(content, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to upload blob '{blobName}': {ex.Message}", ex);
        }
    }

    public async Task<Stream> DownloadAsync(string blobName)
    {
        try
        {
            var blob = _containerClient.GetBlobClient(blobName);
            
            // Check if blob exists first
            var exists = await blob.ExistsAsync();
            if (!exists.Value)
            {
                throw new FileNotFoundException($"Blob '{blobName}' not found.");
            }

            var response = await blob.DownloadStreamingAsync();
            
            // Create a new MemoryStream to hold the blob content
            var memoryStream = new MemoryStream();
            
            // Copy the blob content to the memory stream
            await response.Value.Content.CopyToAsync(memoryStream);
            
            // Reset position to beginning so it can be read
            memoryStream.Position = 0;
            
            return memoryStream;
        }
        catch (FileNotFoundException)
        {
            // Re-throw FileNotFoundException as is
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to download blob '{blobName}': {ex.Message}", ex);
        }
    }

    public async Task<bool> ExistsAsync(string blobName)
    {
        try
        {
            var blob = _containerClient.GetBlobClient(blobName);
            var response = await blob.ExistsAsync();
            return response.Value;
        }
        catch (Exception)
        {
            // If any error occurs, assume blob doesn't exist
            return false;
        }
    }

    public async Task DeleteAsync(string blobName)
    {
        try
        {
            var blob = _containerClient.GetBlobClient(blobName);
            await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging configured
            // For now, we'll silently ignore delete errors since DeleteIfExistsAsync should handle most cases
            // You might want to add logging here: _logger.LogWarning(ex, "Failed to delete blob '{BlobName}'", blobName);
        }
    }
}
