using Microsoft.AspNetCore.Http;

namespace ImageApi.Tests.Helpers;

public class MockFormFile : IFormFile
{
    private readonly MemoryStream _stream;

    public MockFormFile(byte[] content, string fileName, string contentType = "image/png")
    {
        _stream = new MemoryStream(content);
        FileName = fileName;
        ContentType = contentType;
        Length = content.Length;
    }

    public string ContentType { get; set; }
    public string ContentDisposition { get; set; } = string.Empty;
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public long Length { get; }
    public string Name { get; set; } = "file";
    public string FileName { get; set; }

    public Stream OpenReadStream() => new MemoryStream(_stream.ToArray());

    public void CopyTo(Stream target) => _stream.CopyTo(target);

    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => _stream.CopyToAsync(target, cancellationToken);

    public void Dispose() => _stream.Dispose();
}
