# Image API Service

A comprehensive .NET 8 Web API for image upload, storage, and dynamic resizing operations with Azure Blob Storage integration.

## 🌐 Live Demo

**🚀 The application is deployed and accessible at:**
- **API Base URL**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net
- **Swagger Documentation**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net/swagger/index.html

> **Note**: The application is deployed on Azure's free tier (F1 pricing tier) which may result in:
> - Slower response times for the first request (cold start)
> - Limited concurrent connections
> - Automatic sleep after 20 minutes of inactivity
> - Reduced performance compared to paid tiers
> 
> For production use, consider upgrading to a higher tier for optimal performance.

## 🚀 Features

- **Image Upload & Storage** - Upload images in multiple formats (JPG, PNG, GIF, BMP, WebP, TIFF)
- **Dynamic Resizing** - Real-time image resizing with aspect ratio preservation
- **Predefined Resolutions** - Thumbnail (160px), Small (320px), Medium (640px), Large (1024px), XLarge (1920px)
- **CRUD Operations** - Complete image management (Create, Read, Update, Delete)
- **Performance Optimized** - Memory caching, background processing, opportunistic generation
- **Azure Integration** - Azure Blob Storage for scalable file storage
- **RESTful API** - Clean, documented endpoints with Swagger/OpenAPI
- **Comprehensive Logging** - Structured logging throughout the application

## 🏗️ Architecture

```
├── ImageApi/                 # Web API layer
│   ├── Controllers/          # API controllers
│   └── Program.cs           # Application entry point
├── Application/             # Business logic layer
│   ├── Services/           # Service implementations
│   ├── Interfaces/         # Service contracts
│   └── DTOs/              # Data transfer objects
├── Infrastructure/         # Infrastructure layer
│   ├── Data/              # Database context & repositories
│   ├── Storage/           # Azure Blob Storage service
│   └── Config/            # Configuration models
├── Domain/                # Domain entities
└── Tests/                 # Unit tests
```

## 🛠️ Technology Stack

- **.NET 8** - Web API framework
- **Entity Framework Core** - Database ORM
- **SQL Server** - Database
- **Azure Blob Storage** - File storage
- **ImageSharp** - Image processing
- **Memory Caching** - Performance optimization
- **Swagger/OpenAPI** - API documentation
- **xUnit** - Unit testing

## 📋 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (LocalDB or full instance)
- [Azure Storage Account](https://azure.microsoft.com/en-us/services/storage/) (for blob storage)

## ⚙️ Configuration

### 1. Database Connection

Update `appsettings.json` with your SQL Server connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ImageApiDb;Trusted_Connection=true;MultipleActiveResultSets=true"
  }
}
```

### 2. Azure Blob Storage

Configure Azure Blob Storage settings:

```json
{
  "AzureBlobSettings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net",
    "ContainerName": "images"
  }
}
```

### 3. Environment-Specific Settings

Create `appsettings.Development.json` for local development:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 🚀 Getting Started

### Try the Live API

You can immediately test the API using the live deployment:

1. **Access Swagger UI**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net/swagger/index.html
2. **Upload an image** using the `/api/images/upload` endpoint
3. **Test resizing** with various endpoints

### Local Development

1. **Clone the repository**
   ```bash
   git clone https://github.com/husein-shkrijelj/image-api.git
   cd image-api
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Update database**
   ```bash
   dotnet ef database update
   ```

4. **Run the application**
   ```bash
   dotnet run --project ImageApi
   ```

5. **Access Swagger UI**
   ```
   https://localhost:7093/swagger
   ```

### Development URLs

- **HTTPS**: https://localhost:7093
- **HTTP**: http://localhost:5242
- **Swagger**: https://localhost:7093/swagger

## 📚 API Endpoints

### Image Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/images/upload` | Upload a new image |
| `GET` | `/api/images` | Get all images |
| `GET` | `/api/images/{id}` | Get image metadata |
| `PUT` | `/api/images/{id}` | Update an image |
| `DELETE` | `/api/images/{id}` | Delete an image |

### Image Download & Resizing

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/images/{id}/download` | Download original image |
| `GET` | `/api/images/{id}/download/{resolution}` | Download predefined resolution |
| `GET` | `/api/images/{id}/resize?height={px}` | Download custom height resize |
| `GET` | `/api/images/{id}/resize?width={px}` | Download custom width resize |
| `GET` | `/api/images/{id}/resize/{height}/url` | Get URL for resized image |

### Utility Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/images/{id}/resolutions` | Get available resolutions |
| `POST` | `/api/images/{id}/generate-resolutions` | Pre-generate all resolutions |

### Example Usage

**Upload Image (Live API):**
```bash
curl -X POST "https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net/api/images/upload" \
  -H "Content-Type: multipart/form-data" \
  -F "file=@image.jpg"
```

**Get Resized Image (Live API):**
```bash
curl "https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net/api/images/{id}/resize?height=300" \
  --output resized-image.png
```

**Local Development:**
```bash
curl -X POST "https://localhost:7093/api/images/upload" \
  -H "Content-Type: multipart/form-data" \
  -F "file=@image.jpg"
```

## 🧪 Testing

Run unit tests:

```bash
dotnet test
```

Run specific test project:

```bash
dotnet test Tests/ImageServiceTests.cs
```

## 🚀 Deployment

### Azure Deployment

The project includes GitHub Actions workflow for automatic deployment to Azure Web Apps.

**Current Deployment:**
- **Platform**: Azure App Service (Free Tier - F1)
- **Region**: Canada Central
- **URL**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net

**Prerequisites:**
- Azure Web App created
- GitHub secrets configured:
  - `AZUREAPPSERVICE_CLIENTID_*`
  - `AZUREAPPSERVICE_TENANTID_*`
  - `AZUREAPPSERVICE_SUBSCRIPTIONID_*`

**Deployment Process:**
1. Push to `main` branch
2. GitHub Actions automatically builds and deploys
3. Application available at the Azure URL

### Manual Deployment

1. **Publish the application**
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. **Deploy to your hosting platform**
   - Azure Web Apps
   - AWS Elastic Beanstalk
   - Docker containers

## 🔧 Performance Features

- **Memory Caching** - Image metadata and blob existence caching
- **Background Processing** - Automatic thumbnail generation
- **Opportunistic Generation** - Common sizes generated proactively
- **Async Operations** - Non-blocking I/O operations
- **Optimized Storage** - Efficient blob storage patterns

> **Performance Note**: The live demo runs on Azure's free tier which may experience slower response times. For production workloads, consider upgrading to Standard or Premium tiers for better performance and reliability.

## 📝 Logging

The application uses structured logging with different log levels:

- **Information** - General application flow
- **Warning** - Unexpected situations
- **Error** - Error events with stack traces
- **Debug** - Detailed diagnostic information

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/husein-shkrijelj/image-api/blob/main/LICENSE) file for details.

## 🆘 Support

For support and questions:

- Create an [Issue](https://github.com/husein-shkrijelj/image-api/issues)
- Contact: [husein.shkrijelj@gmail.com](mailto:husein.shkrijelj@gmail.com)

## 🔗 Links

- **Live API**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net
- **Live Documentation**: https://image-api-service-awc7afhkbwbba5cg.canadacentral-01.azurewebsites.net/swagger/index.html
- **GitHub Repository**: https://github.com/husein-shkrijelj/image-api
