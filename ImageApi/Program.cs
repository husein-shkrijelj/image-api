using ImageApi.Application.Interfaces;
using ImageApi.Application.Services;
using ImageApi.Infrastructure.Config;
using ImageApi.Infrastructure.Data;
using ImageApi.Infrastructure.Storage;
using Infrastructure.Data.Repository;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Load configuration files and environment variables
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// 1. Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Add Memory Cache FIRST - before other services that depend on it
builder.Services.AddMemoryCache();

// 3. Register DbContext with connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ImageDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    }));

// Bind AzureBlobSettings from configuration
builder.Services.Configure<AzureBlobSettings>(
    builder.Configuration.GetSection(nameof(AzureBlobSettings)));

// 4. Register application and infrastructure services
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();

var app = builder.Build();

app.UseSwagger();

app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();