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

            // 1️⃣ Lấy dữ liệu chi tiết từ DB (RAG)
            var books = await _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .Take(5)
                .ToListAsync();

            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} ({b.BookType?.Name ?? "Không rõ thể loại"})\n";
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
                            new { text = $"{contextData}\n\nNgười dùng hỏi: {request.Question}" }
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

            return Ok(new { answer = message });
        }

        [HttpPost("ask/details")]
        public async Task<IActionResult> AskWithFilter([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // Lọc sách theo từ khoá (ví dụ Title hoặc Author)
            var books = await _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .Where(b => b.Title.Contains(request.Question) || b.Author.Contains(request.Question))
                .Take(5)
                .ToListAsync();

            if (!books.Any())
                return Ok(new { answer = "Không tìm thấy sách liên quan." });

            string contextData = "Dữ liệu chi tiết liên quan đến câu hỏi:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} ({b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Số lượng còn: {b.Quantity}\n";
                contextData += $"  Mô tả: {b.Description ?? "Không có mô tả"}\n";
            }

            // Gửi request đến Gemini
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
                            new { text = $"{contextData}\n\nNgười dùng hỏi: {request.Question}" }
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

            return Ok(new { answer = message });
        }

        [HttpPost("ask/store")]
        public async Task<IActionResult> AskByStore([FromBody] StoreChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message cannot be empty");

            if (request.BookstoreId <= 0)
                return BadRequest("BookstoreId phải lớn hơn 0");

            // 1️⃣ Lọc sách theo bookstoreId
            var books = await _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .Where(b => b.BookstoreId == request.BookstoreId)
                .Take(10) // Giới hạn top 10
                .ToListAsync();

            if (!books.Any())
                return Ok(new { answer = "Không tìm thấy sách nào trong cửa hàng này." });

            // 2️⃣ Tạo context chi tiết
            string contextData = $"Dữ liệu các sách trong cửa hàng {request.BookstoreId}:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} ({b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Số lượng còn: {b.Quantity}\n";
                contextData += $"  Mô tả: {b.Description ?? "Không có mô tả"}\n";
            }

            // 3️⃣ Gửi request đến Gemini
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
                    new { text = $"{contextData}\n\nNgười dùng hỏi: {request.Message}" }
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

            return Ok(new { answer = message });
        }

        // Request model cho bookstore
        public class StoreChatRequest
        {
            public string Message { get; set; }
            public int BookstoreId { get; set; }
        }
    }



    public class ChatRequest
    {
        public string Question { get; set; }
    }
}
