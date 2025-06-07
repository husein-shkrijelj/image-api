using ImageApi.Application.Interfaces;
using ImageApi.Domain.Entities;
using ImageApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repository
{
    public class ImageRepository : IImageRepository
    {
        private readonly ImageDbContext _context;

        public ImageRepository(ImageDbContext context)
        {
            _context = context;
        }

        public async Task<ImageInfo?> GetByIdAsync(string id)
        {
            return await _context.ImageInfos.FindAsync(id);
        }

        public async Task<IEnumerable<ImageInfo>> GetAllAsync()
        {
            return await _context.ImageInfos.ToListAsync();
        }

        public async Task AddAsync(ImageInfo image)
        {
            await _context.ImageInfos.AddAsync(image);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(ImageInfo image)
        {
            _context.ImageInfos.Update(image);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(string id)
        {
            var image = await _context.ImageInfos.FindAsync(id);
            if (image != null)
            {
                _context.ImageInfos.Remove(image);
                await _context.SaveChangesAsync();
            }
        }
    }
} 