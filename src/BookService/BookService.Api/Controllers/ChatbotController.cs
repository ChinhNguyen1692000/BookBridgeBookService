using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

        // --------------------------------------------------------------------------------------
        // ENDPOINT /api/chatbot/ask (Dùng cho toàn hệ thống)
        // --------------------------------------------------------------------------------------
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // 1️⃣ Lấy dữ liệu chi tiết từ DB (RAG)
            // LƯU Ý: Không cần Include(b => b.BookImages) vì ImageUrl nằm trực tiếp trên Entity Book.
            var books = await _context.Books
                .Include(b => b.BookType)
                .OrderByDescending(b => b.RatingsCount)
                .Take(5)
                .ToListAsync();

            // Cập nhật: Tạo danh sách BookInfo đầy đủ các trường, lấy ImageUrl từ b.ImageUrl
            var bookInfos = books.Select(b => new BookInfo
            {
                Id = b.Id,
                Title = b.Title,
                BookstoreId = b.BookstoreId, // Không dùng ?? 0 vì nó là int [Required]
                Price = b.Price,
                ImageUrl = b.ImageUrl // LẤY TRỰC TIẾP TỪ ENTITY BOOK
            }).ToList();


            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} (ID: {b.Id}, {b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Rating trung bình: {b.AverageRating ?? 0}\n";
                // ... (Các chi tiết khác)
            }

            // 2️⃣ Chuẩn bị request đến Gemini API
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            var prompt = $"Bạn là một trợ lý thông minh về sách. Hãy trả lời câu hỏi của người dùng dựa trên ngữ cảnh sau. Khi nhắc đến sách, hãy thêm ID [ID] vào sau tên sách.\n\n{contextData}\n\nNgười dùng hỏi: {request.Question}";

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
            {
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });
            }

            // Trả về kết quả cho frontend, kèm theo Books
            return Ok(new ChatbotResponse { Answer = message, Books = bookInfos });
        }

        // --------------------------------------------------------------------------------------
        // ENDPOINT /api/chatbot/ask/store (Dùng cho từng cửa hàng)
        // --------------------------------------------------------------------------------------
        [HttpPost("ask/store")]
        public async Task<IActionResult> AskByStore([FromBody] StoreChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message cannot be empty");

            if (request.BookstoreId <= 0)
                return BadRequest("BookstoreId phải lớn hơn 0");

            // ... (Logic lọc sách theo từ khóa và giá tiền - giữ nguyên) ...
            var searchTerms = request.Message.ToLower().Split(new[] { ' ', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var minPrice = 0m;
            var maxPrice = decimal.MaxValue;

            foreach (var term in searchTerms)
            {
                if (decimal.TryParse(term, out var priceValue))
                {
                    minPrice = priceValue;
                    maxPrice = decimal.MaxValue;
                    break;
                }
            }

            var query = _context.Books
                .Include(b => b.BookType)
                // LƯU Ý: Không cần Include(b => b.BookImages) vì ImageUrl nằm trực tiếp trên Entity Book.
                .Where(b => b.BookstoreId == request.BookstoreId);

            if (minPrice == 0)
            {
                foreach (var term in searchTerms)
                {
                    var termTrimmed = term.Trim();
                    query = query.Where(b =>
                        EF.Functions.ILike(b.Title, $"%{termTrimmed}%") ||
                        (b.Author != null && EF.Functions.ILike(b.Author, $"%{termTrimmed}%")) ||
                        (b.BookType != null && EF.Functions.ILike(b.BookType.Name, $"%{termTrimmed}%"))
                    );
                }
            }
            else
            {
                query = query.Where(b => b.Price >= minPrice && b.Price <= maxPrice);
            }

            var books = await query
                .OrderByDescending(b => b.AverageRating)
                .Take(10)
                .ToListAsync();

            if (!books.Any())
            {
                // Nếu không tìm thấy sách, lấy sách phổ biến nhất
                books = await _context.Books
                    .Include(b => b.BookType)
                    .Where(b => b.BookstoreId == request.BookstoreId)
                    .OrderByDescending(b => b.RatingsCount)
                    .Take(5)
                    .ToListAsync();

                if (!books.Any())
                    return Ok(new ChatbotResponse { Answer = "Xin lỗi, không tìm thấy sách nào trong cửa hàng này.", Books = new List<BookInfo>() });
            }

            // Cập nhật: Tạo danh sách BookInfo đầy đủ các trường, lấy ImageUrl từ b.ImageUrl
            var bookInfos = books.Select(b => new BookInfo
            {
                Id = b.Id,
                Title = b.Title,
                BookstoreId = b.BookstoreId, // Đã sửa lỗi CS0019 (nếu Entity là int)
                Price = b.Price,
                ImageUrl = b.ImageUrl // LẤY TRỰC TIẾP TỪ ENTITY BOOK
            }).ToList();

            // 2️⃣ Tạo context chi tiết (dùng cho prompt)
            string contextData = $"Dữ liệu các sách trong cửa hàng {request.BookstoreId} liên quan đến yêu cầu:\n";
            foreach (var b in books)
            {
                contextData += $"- **{b.Title}** (ID: {b.Id}, {b.BookType?.Name ?? "Không rõ thể loại"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Số lượng còn: {b.Quantity}\n";
                contextData += $"  Mô tả: {b.Description ?? "Không có mô tả"}\n";
            }

            // 3️⃣ Gửi request đến Gemini (Phần prompt giữ nguyên để yêu cầu Structured Output)
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            var prompt = $@"
Bạn là một trợ lý thông minh có thể truy cập database của hệ thống. 
Nhiệm vụ của bạn là trả lời người dùng dựa trên ngữ cảnh sách được cung cấp dưới đây, và **luôn luôn** đính kèm dữ liệu sách liên quan dưới dạng JSON theo format bắt buộc.

--- CONTEXT DỮ LIỆU SÁCH ---
Dữ liệu các sách trong cửa hàng {request.BookstoreId} liên quan đến yêu cầu:
{contextData}
--- END CONTEXT ---

Người dùng hỏi: {request.Message}

--- HƯỚNG DẪN TRẢ LỜI ---
1.  **Phản hồi:** Trả lời một cách tự nhiên, hữu ích và lịch sự, sử dụng tiếng Việt.
2.  **Định dạng Sách:** Khi nhắc đến tên sách trong phần trả lời tự nhiên, hãy kèm theo ID của sách đó trong ngoặc vuông (ví dụ: Tên Sách [ID]) để frontend có thể tạo liên kết.
3.  **Dữ liệu JSON (BẮT BUỘC):** Luôn đính kèm danh sách sách bạn tham chiếu/đề xuất vào cuối phản hồi theo định dạng JSON sau:

    **a. Bắt đầu với:** `----`
    **b. Dữ liệu:** Một mảng JSON của các đối tượng sách (chỉ chứa Id, Title, BookstoreId). Chỉ bao gồm các sách bạn đã đề cập hoặc tham khảo trong câu trả lời tự nhiên.
    **c. Kết thúc với:** `----`

**Ví dụ về JSON Sách:**
----
[
    {{ ""Id"": 101, ""Title"": ""Tên Sách Hay"", ""BookstoreId"": {request.BookstoreId} }},
    {{ ""Id"": 102, ""Title"": ""Sách Tiếp Theo"", ""BookstoreId"": {request.BookstoreId} }}
]
----

Hãy bắt đầu phản hồi của bạn ngay bây giờ.
";

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
            var rawResponseText = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            if (string.IsNullOrEmpty(rawResponseText))
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });

            // ----------------------------------------------------
            // LOGIC PHÂN TÁCH PHẢN HỒI (TÁCH TEXT VÀ JSON)
            // ----------------------------------------------------
            const string startDelimiter = "----";
            string naturalAnswer = rawResponseText;

            int startIndex = rawResponseText.IndexOf(startDelimiter);

            if (startIndex != -1)
            {
                // Lấy phần văn bản tự nhiên (phần trước JSON)
                naturalAnswer = rawResponseText.Substring(0, startIndex).Trim();
            }

            // Trả về dữ liệu cấu trúc (JSON) để hỗ trợ liên kết
            return Ok(new ChatbotResponse { Answer = naturalAnswer, Books = bookInfos });
        }

        // --------------------------------------------------------------------------------------
        // MODELS
        // --------------------------------------------------------------------------------------

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
            public List<BookInfo>? Books { get; set; }
        }

        // Model chứa thông tin cần thiết để tạo liên kết và hiển thị
        public class BookInfo
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("bookstoreId")]
            public int BookstoreId { get; set; }

            [JsonPropertyName("price")]
            public decimal Price { get; set; }

            // Đổi lại thành string (không null) để khớp với định nghĩa trong Entity Book
            [JsonPropertyName("imageUrl")]
            public string ImageUrl { get; set; }
        }
    }

    public class ChatRequest
    {
        public string Question { get; set; }
    }
}