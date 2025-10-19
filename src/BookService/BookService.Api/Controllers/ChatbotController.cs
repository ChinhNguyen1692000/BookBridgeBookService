using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookService.Infracstructure.DBContext;
using BookService.Domain.Entities;

namespace BookService.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private readonly BookDBContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public ChatbotController(BookDBContext context, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpGet("ping")]
        public IActionResult Ping() => Ok("Chatbot API is alive!");

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // 1️⃣ Lấy dữ liệu chi tiết từ DB (RAG) - giữ nguyên logic cũ
            var books = await _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .Take(5)
                .ToListAsync();

            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} (ID: {b.Id}, {b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Dịch giả: {b.Translator ?? "Không rõ"}\n";
                contextData += $"  Nhà xuất bản: {b.Publisher ?? "Không rõ"}, Ngày xuất bản: {b.PublishedDate?.ToString("yyyy-MM-dd") ?? "Không rõ"}\n";
                contextData += $"  Ngôn ngữ: {b.Language ?? "Không rõ"}, Số trang: {b.PageCount ?? 0}\n";
                contextData += $"  Giá: {b.Price:C}, Rating trung bình: {b.AverageRating ?? 0} ({b.RatingsCount ?? 0} đánh giá)\n";
                contextData += $"  Số lượng còn: {b.Quantity}\n";
                contextData += $"  Mô tả: {b.Description ?? "Không có mô tả"}\n";
                if (b.BookImages.Any())
                    contextData += $"  Hình ảnh: {string.Join(", ", b.BookImages.Select(i => i.ImageUrl))}\n";
            }

            // 2️⃣ Chuẩn bị request đến Gemini API
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];

            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"Bạn là một trợ lý thông minh về sách. Hãy trả lời câu hỏi của người dùng dựa trên ngữ cảnh sau:\n\n{contextData}\n\nNgười dùng hỏi: {request.Question}" }
                        }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var response = await http.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            );

            var json = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonNode.Parse(json);
            var message = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            if (string.IsNullOrEmpty(message))
            {
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });
            }

            // Trả về kết quả cho frontend
            return Ok(new ChatbotResponse { Answer = message });
        }

        // **Endpoint Ask/Store mới - Gộp chức năng Details và Store**
        // **URL: /api/chatbot/ask/store**
        [HttpPost("ask/store")]
        public async Task<IActionResult> AskByStore([FromBody] StoreChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message cannot be empty");

            if (request.BookstoreId <= 0)
                return BadRequest("BookstoreId phải lớn hơn 0");

            // Xử lý từ khóa tìm kiếm
            var searchTerms = request.Message.ToLower().Split(new[] { ' ', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var minPrice = 0m;
            var maxPrice = decimal.MaxValue;

            // Cố gắng trích xuất giá tiền từ câu hỏi
            foreach (var term in searchTerms)
            {
                if (decimal.TryParse(term, out var priceValue))
                {
                    // Giả định nếu giá là số nguyên, đó là giá tối thiểu
                    minPrice = priceValue;
                    // Reset maxPrice để tìm kiếm trong khoảng giá nhất định (nếu cần)
                    maxPrice = decimal.MaxValue;
                    break;
                }
            }

            // Lọc sách theo BookstoreId và Tiêu chí tìm kiếm linh hoạt
            var query = _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .Where(b => b.BookstoreId == request.BookstoreId);

            // Thêm điều kiện tìm kiếm linh hoạt (chỉ tìm kiếm theo Title, Author, TypeName nếu không trích xuất được Price)
            if (minPrice == 0)
            {
                foreach (var term in searchTerms)
                {
                    var termTrimmed = term.Trim();
                    // Tìm kiếm gần đúng/có chứa từ khóa trong Title, Author, hoặc BookType Name
                    query = query.Where(b =>
                        EF.Functions.ILike(b.Title, $"%{termTrimmed}%") ||
                        (b.Author != null && EF.Functions.ILike(b.Author, $"%{termTrimmed}%")) ||
                        (b.BookType != null && EF.Functions.ILike(b.BookType.Name, $"%{termTrimmed}%"))
                    );
                }
            }
            else
            {
                // Lọc theo giá nếu có
                query = query.Where(b => b.Price >= minPrice && b.Price <= maxPrice);
            }

            var books = await query
                .OrderByDescending(b => b.AverageRating) // Ưu tiên sách có rating cao
                .Take(10) // Giới hạn top 10 sách liên quan
                .ToListAsync();

            if (!books.Any())
            {
                // Nếu không tìm thấy sách nào khớp với từ khóa, lấy sách phổ biến nhất trong store đó
                books = await _context.Books
                    .Include(b => b.BookType)
                    .Include(b => b.BookImages)
                    .Where(b => b.BookstoreId == request.BookstoreId)
                    .OrderByDescending(b => b.RatingsCount)
                    .Take(5)
                    .ToListAsync();

                if (!books.Any())
                    return Ok(new ChatbotResponse { Answer = "Xin lỗi, không tìm thấy sách nào trong cửa hàng này." });
            }

            // Chuẩn bị danh sách BookInfo cho Frontend
            var bookInfos = books.Select(b => new BookInfo { Id = b.Id, Title = b.Title }).ToList();

            // 2️⃣ Tạo context chi tiết
            string contextData = $"Dữ liệu các sách trong cửa hàng {request.BookstoreId} liên quan đến yêu cầu:\n";
            foreach (var b in books)
            {
                contextData += $"- **{b.Title}** (ID: {b.Id}, {b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Số lượng còn: {b.Quantity}\n";
                contextData += $"  Mô tả: {b.Description ?? "Không có mô tả"}\n";
            }

            // 3️⃣ Gửi request đến Gemini
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            // Yêu cầu AI trả lời và liệt kê các ID sách trong phần trả lời (có thể ẩn/mô tả trong output)
            var prompt = $"Dựa vào dữ liệu sách sau trong cửa hàng {request.BookstoreId}:\n\n{contextData}\n\nNgười dùng hỏi: {request.Message}\n\nHãy trả lời một cách tự nhiên và hữu ích. **Đặc biệt, khi bạn nhắc đến tên sách, hãy kèm theo ID của sách đó trong ngoặc vuông (ví dụ: Tên Sách [ID]) để frontend có thể tạo liên kết.** Ví dụ: 'Bạn có thể tham khảo cuốn sách Hay Nhất [101] của tác giả Nguyễn Văn A.'";

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var response = await http.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            );

            var json = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonNode.Parse(json);
            var message = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            if (string.IsNullOrEmpty(message))
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });

            // 4️⃣ Trả về dữ liệu cấu trúc (JSON) để hỗ trợ liên kết
            return Ok(new ChatbotResponse { Answer = message, Books = bookInfos });
        }

        // Request model cho bookstore
        public class StoreChatRequest
        {
            public string Message { get; set; }
            public int BookstoreId { get; set; }
        }

        // Response model chung cho frontend
        public class ChatbotResponse
        {
            public string Answer { get; set; }
            public List<BookInfo>? Books { get; set; } // Dùng cho AskByStore để tạo liên kết
        }

        // Model chứa thông tin cần thiết để tạo liên kết
        public class BookInfo
        {
            public int Id { get; set; }
            public string Title { get; set; }
        }
    }

    public class ChatRequest
    {
        public string Question { get; set; }
    }
}