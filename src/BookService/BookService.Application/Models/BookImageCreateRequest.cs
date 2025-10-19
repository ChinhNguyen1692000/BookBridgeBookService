using Microsoft.AspNetCore.Http;

public class BookImageCreateRequest
{
    public int BookId { get; set; }

    // File upload
    public IFormFile ImageFile { get; set; } = default!;
}

// Dùng khi trả về thông tin ảnh
public class BookImageDTO
{
    public int Id { get; set; }
    public int BookId { get; set; }

    // URL ảnh trên Cloudinary
    public string ImageUrl { get; set; } = default!;

    // Thời gian upload
    public DateTime UploadedAt { get; set; }
}