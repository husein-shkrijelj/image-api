using ImageApi.Application.DTOs;
using Microsoft.AspNetCore.Http;
using ImageApi.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IImageService
{
    // Create
    Task<ImageUploadResultDto> UploadImageAsync(IFormFile file);
    
    // Read
    Task<IEnumerable<ImageInfo>> GetAllImagesAsync();
    Task<ImageInfo?> GetImageByIdAsync(string id);
    Task<ImageDownloadResultDto?> DownloadImageAsync(string id);
    Task<ImageDownloadResultDto?> DownloadImageWithResolutionAsync(string id, string resolution);
    Task<ImageDownloadResultDto?> GetResizedImageAsync(string id, int? width, int? height);
    Task<IEnumerable<string>?> GetAvailableResolutionsAsync(string id);
    
    // Update
    Task<ImageUploadResultDto?> UpdateImageAsync(string id, IFormFile file);
    
    // Delete
    Task<bool> DeleteImageAsync(string id);
    
    // Resolution management
    Task<ResolutionGenerationResultDto?> GeneratePredefinedResolutionsAsync(string id);
    
    // Legacy method for backward compatibility
    Task<string?> GetResizedImagePathAsync(string imageId, int height);
}