using ImageApi.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageApi.Application.Interfaces
{
    public interface IImageRepository
    {
        Task<ImageInfo?> GetByIdAsync(string id);
        Task<IEnumerable<ImageInfo>> GetAllAsync();
        Task AddAsync(ImageInfo image);
        Task UpdateAsync(ImageInfo image);
        Task DeleteAsync(string id);
    }
} 