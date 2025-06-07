using ImageApi.Application.Interfaces;
using ImageApi.Application.Services;
using ImageApi.Infrastructure.Config;
using ImageApi.Infrastructure.Data;
using ImageApi.Infrastructure.Storage;
using Infrastructure.Data.Repository;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Register strongly-typed settings from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ImageDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    }));

builder.Services.Configure<AzureBlobSettings>(
    builder.Configuration.GetSection(nameof(AzureBlobSettings)));

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// 3. Register application and infrastructure services
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();

var app = builder.Build();

// 4. Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
