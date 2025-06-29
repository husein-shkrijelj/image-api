﻿using ImageApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ImageApi.Infrastructure.Data
{
    public class ImageDbContext : DbContext
    {
        public ImageDbContext(DbContextOptions<ImageDbContext> options) : base(options)
        {
        }

        public DbSet<ImageInfo> ImageInfos => Set<ImageInfo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ImageInfo>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(50).IsRequired();
                
                entity.Property(e => e.OriginalFileName).HasMaxLength(255).IsRequired();
                entity.Property(e => e.BlobName).HasMaxLength(255).IsRequired();
                entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
                
                entity.Property(e => e.Size).IsRequired();
                entity.Property(e => e.OriginalHeight).IsRequired();
                entity.Property(e => e.OriginalWidth).IsRequired();
                entity.Property(e => e.UploadedAt).IsRequired();
                
                // Existing optional fields
                entity.Property(e => e.UpdatedAt).IsRequired(false); // Nullable
                entity.Property(e => e.FileExtension).HasMaxLength(10).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500).IsRequired(false); // Nullable
                entity.Property(e => e.Metadata).HasMaxLength(1000).IsRequired(false); // Nullable
                
                // New compression fields
                entity.Property(e => e.IsCompressed).IsRequired();
                entity.Property(e => e.CompressionType).HasMaxLength(20).IsRequired();
                
                // Set default values
                entity.Property(e => e.UploadedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.FileExtension).HasDefaultValue(".png");
                entity.Property(e => e.IsCompressed).HasDefaultValue(false);
                entity.Property(e => e.CompressionType).HasDefaultValue("none");
            });
        }
    }

    // Design-time factory for EF Core migrations
    public class ImageDbContextFactory : IDesignTimeDbContextFactory<ImageDbContext>
    {
        public ImageDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../ImageApi"))
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<ImageDbContext>();
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Add retry logic for Azure SQL Database
            optionsBuilder.UseSqlServer(connectionString, options =>
            {
                options.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });

            return new ImageDbContext(optionsBuilder.Options);
        }
    }
}