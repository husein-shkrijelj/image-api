namespace ImageApi.Infrastructure.Config
{
    public class AzureBlobSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
    }
}
