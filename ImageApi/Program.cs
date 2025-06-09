using ImageApi.Application.Interfaces;
using ImageApi.Application.Services;
using ImageApi.Helpers;
using ImageApi.Infrastructure.Config;
using ImageApi.Infrastructure.Data;
using ImageApi.Infrastructure.Storage;
using Infrastructure.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

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

// 2. Configure Swagger with XML documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Image API",
        Version = "v1",
        Description = "A comprehensive API for image upload, storage, and dynamic resizing operations.",
        Contact = new OpenApiContact
        {
            Name = "Husein Skrijelj",
            Email = "husein.shkrijelj@gmail.com"
        }
    });

    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);

    // Optional: Include XML comments from other assemblies (like Application layer)
    var applicationXmlFile = "Application.xml";
    var applicationXmlPath = Path.Combine(AppContext.BaseDirectory, applicationXmlFile);
    if (File.Exists(applicationXmlPath))
    {
        c.IncludeXmlComments(applicationXmlPath);
    }

    // Configure file upload support in Swagger UI
    c.OperationFilter<FileUploadOperationFilter>();
});

// 3. Add Memory Cache FIRST - before other services that depend on it
builder.Services.AddMemoryCache();

// 4. Register DbContext with connection string from configuration
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

// 5. Register application and infrastructure services
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();

var app = builder.Build();

// 6. Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Image API v1");
    c.RoutePrefix = "swagger"; // Makes Swagger UI available at root URL
    c.DocumentTitle = "Image API Documentation";
    c.DefaultModelsExpandDepth(-1); // Hide schemas section by default
    c.DisplayRequestDuration();
});

app.UseHttpsRedirection();

app.MapControllers();

app.Run();