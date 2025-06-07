using ImageApi.Application.DTOs;
using Microsoft.AspNetCore.Http;
using ImageApi.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IImageService
{
    Task<ImageUploadResultDto> UploadImageAsync(IFormFile file);
    Task<string?> GetResizedImagePathAsync(string imageId, int height);
    Task<IEnumerable<ImageInfo>> GetAllImagesAsync();
}