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

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // 1️⃣ Lấy dữ liệu chi tiết từ DB (RAG)
            // Lấy 5 cuốn sách đầu tiên (hoặc phổ biến)
            var books = await _context.Books
                .Include(b => b.BookType)
                .Include(b => b.BookImages)
                .OrderByDescending(b => b.RatingsCount) // Giả sử lấy 5 cuốn phổ biến nhất
                .Take(5)
                .ToListAsync();

            // Tạo danh sách BookInfo để trả về (sử dụng BookstoreId mặc định là 0 hoặc gán BookstoreId thật nếu có)
            var bookInfos = books.Select(b => new BookInfo
            {
                Id = b.Id,
                Title = b.Title,
                BookstoreId = b.BookstoreId // Gán 0 nếu BookstoreId là null, hoặc gán giá trị thật
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
        // CẬP NHẬT ENDPOINT AskByStore
        // --------------------------------------------------------------------------------------

        [HttpPost("ask/store")]
        public async Task<IActionResult> AskByStore([FromBody] StoreChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message cannot be empty");

            if (request.BookstoreId <= 0)
                return BadRequest("BookstoreId phải lớn hơn 0");

            // Xử lý từ khóa tìm kiếm và truy vấn DB (Phần này giữ nguyên logic tốt)
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
                .Include(b => b.BookImages)
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
                    .Include(b => b.BookImages)
                    .Where(b => b.BookstoreId == request.BookstoreId)
                    .OrderByDescending(b => b.RatingsCount)
                    .Take(5)
                    .ToListAsync();

                if (!books.Any())
                    return Ok(new ChatbotResponse { Answer = "Xin lỗi, không tìm thấy sách nào trong cửa hàng này.", Books = new List<BookInfo>() });
            }

            // Đã sửa lỗi CS1061 và đảm bảo gán Books
            var bookInfos = books.Select(b => new BookInfo
            {
                Id = b.Id,
                Title = b.Title,
                // Loại bỏ .Value vì BookstoreId thường là int, hoặc int? (đã xử lý null ở trên)
                BookstoreId = b.BookstoreId
            }).ToList();

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

            // ----------------------------------------------------
            // PROMPT YÊU CẦU STRUCTURED OUTPUT VÀ LINKING
            // ----------------------------------------------------
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
            // const string endDelimiter = "----"; // Sử dụng biến đã khai báo
            string naturalAnswer = rawResponseText;

            int startIndex = rawResponseText.IndexOf(startDelimiter);
            int endIndex = rawResponseText.LastIndexOf(startDelimiter); // Chỉ cần tìm startDelimiter lần cuối

            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                // Lấy phần văn bản tự nhiên (phần trước JSON)
                naturalAnswer = rawResponseText.Substring(0, startIndex).Trim();
                
                // (Không cần trích xuất JSON Books vì ta sử dụng bookInfos đã có để đảm bảo độ chính xác)
            }
            // Nếu không tìm thấy JSON, naturalAnswer = rawResponseText, và ta vẫn dùng bookInfos.

            // 4️⃣ Trả về dữ liệu cấu trúc (JSON) để hỗ trợ liên kết
            return Ok(new ChatbotResponse { Answer = naturalAnswer, Books = bookInfos });
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
            [JsonPropertyName("Id")]
            public int Id { get; set; }

            [JsonPropertyName("Title")]
            public string Title { get; set; }

            [JsonPropertyName("BookstoreId")]
            public int BookstoreId { get; set; } 
        }
    }

    public class ChatRequest
    {
        public string Question { get; set; }
    }
}