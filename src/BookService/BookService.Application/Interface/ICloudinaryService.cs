using Microsoft.AspNetCore.Http;

public interface ICloudinaryService
{
    Task<string> UploadImageAsync(IFormFile file);
}