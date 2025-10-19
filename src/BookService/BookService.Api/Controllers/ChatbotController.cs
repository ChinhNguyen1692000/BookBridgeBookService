using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BookService.Infracstructure.DBContext;
using BookService.Domain.Entities;
using System.Linq;
using LinqKit;
using System.Text.RegularExpressions; // Thêm thư viện này để xử lý JSON

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

            // 1️⃣ LOGIC TÌM KIẾM THEO TỪ KHÓA (FUZZY SEARCH)
            var searchTerms = request.Question.ToLower().Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Where(t => t.Length > 2)
                                             .Distinct()
                                             .Take(5)
                                             .ToList();

            var booksQuery = _context.Books
                .Include(b => b.BookType)
                .AsExpandable();

            var predicate = PredicateBuilder.New<Book>(true);

            // Xây dựng điều kiện OR dựa trên từ khóa để tìm kiếm linh hoạt
            if (searchTerms.Any())
            {
                var keywordPredicate = PredicateBuilder.New<Book>(false);

                foreach (var term in searchTerms)
                {
                    var innerTerm = term;
                    keywordPredicate = keywordPredicate.Or(b =>
                        EF.Functions.ILike(b.Title, $"%{innerTerm}%") ||
                        (b.Author != null && EF.Functions.ILike(b.Author, $"%{innerTerm}%")) ||
                        (b.BookType != null && EF.Functions.ILike(b.BookType.Name, $"%{innerTerm}%")) ||
                        (b.Description != null && EF.Functions.ILike(b.Description, $"%{innerTerm}%"))
                    );
                }
                predicate = predicate.And(keywordPredicate);
            }

            var books = await booksQuery.Where(predicate)
                .OrderByDescending(b => b.AverageRating)
                .ThenByDescending(b => b.RatingsCount)
                .Take(5)
                .ToListAsync();

            // Nếu không tìm thấy sách nào dựa trên từ khóa hoặc câu hỏi chung chung (fallback)
            if (!books.Any() && searchTerms.Any() == false)
            {
                books = await _context.Books
                   .Include(b => b.BookType)
                   .OrderByDescending(b => b.RatingsCount)
                   .Take(5)
                   .ToListAsync();
            }

            // ⚠️ KHÔNG CẦN TẠO bookInfos TẠM THỜI NỮA. CHÚNG TA SẼ DỰA VÀO PHẢN HỒI CỦA AI.

            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} (ID: {b.Id}, {b.BookstoreId}, Thể loại: {b.BookType?.Name ?? "Không rõ"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}\n";
            }

            // 2️⃣ Chuẩn bị request đến Gemini API
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            var prompt = $@"
Bạn là một trợ lý thông minh về sách. Hãy trả lời câu hỏi của người dùng dựa trên ngữ cảnh sách được cung cấp dưới đây.
Khi nhắc đến sách trong phần trả lời tự nhiên, hãy thêm ID [ID] vào sau tên sách (ví dụ: Tên Sách [ID]).

--- CONTEXT DỮ LIỆU SÁCH ---
{contextData}
--- END CONTEXT ---

Người dùng hỏi: {request.Question}

--- HƯỚNG DẪN TRẢ LỜI ---
1. Phản hồi: Trả lời một cách tự nhiên, hữu ích và lịch sự, sử dụng tiếng Việt.
2. Dữ liệu JSON (BẮT BUỘC): Luôn đính kèm danh sách sách bạn tham chiếu/đề xuất vào cuối phản hồi theo định dạng JSON sau:

    a. Bắt đầu bằng dòng: `----BOOKS_JSON_START----`
    b. Dữ liệu: Một mảng JSON của các đối tượng BookInfo (chỉ chứa Id, Title, BookstoreId). **Chỉ bao gồm các sách bạn đã đề cập hoặc tham khảo trong câu trả lời tự nhiên.**
    c. Kết thúc bằng dòng: `----BOOKS_JSON_END----`

Ví dụ về JSON Sách:
----BOOKS_JSON_START----
[
    {{ ""Id"": 101, ""Title"": ""Tên Sách Hay"", ""BookstoreId"": 1 }},
    {{ ""Id"": 102, ""Title"": ""Sách Tiếp Theo"", ""BookstoreId"": 1 }}
]
----BOOKS_JSON_END----

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
            {
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });
            }

            // 3️⃣ LOGIC PHÂN TÁCH PHẢN HỒI (TÁCH TEXT VÀ JSON)
            const string startDelimiter = "----BOOKS_JSON_START----";
            const string endDelimiter = "----BOOKS_JSON_END----";
            string naturalAnswer = rawResponseText;
            List<BookInfo> recommendedBooks = new List<BookInfo>();

            // Tìm và trích xuất JSON
            int startIndex = rawResponseText.IndexOf(startDelimiter);
            int endIndex = rawResponseText.IndexOf(endDelimiter, startIndex + startDelimiter.Length);

            if (startIndex != -1 && endIndex != -1)
            {
                // Lấy phần văn bản trước JSON
                naturalAnswer = rawResponseText.Substring(0, startIndex).Trim();

                // Trích xuất chuỗi JSON
                string jsonPart = rawResponseText.Substring(startIndex + startDelimiter.Length, endIndex - (startIndex + startDelimiter.Length)).Trim();

                // Thử deserialize JSON
                try
                {
                    // Loại bỏ bất kỳ ký tự markdown nào mà AI có thể thêm vào
                    jsonPart = Regex.Replace(jsonPart, @"^```json\s*|```\s*$", string.Empty, RegexOptions.Multiline).Trim();

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var deserializedBooks = JsonSerializer.Deserialize<List<BookInfo>>(jsonPart, options);

                    if (deserializedBooks != null)
                    {
                        recommendedBooks = deserializedBooks;
                    }
                }
                catch (JsonException ex)
                {
                    // Log lỗi nếu JSON không hợp lệ, nhưng vẫn trả về câu trả lời tự nhiên
                    Console.WriteLine($"Lỗi phân tích JSON từ AI: {ex.Message}");
                }
            }

            // Trả về kết quả cho frontend, kèm theo danh sách sách đã được AI đề xuất
            return Ok(new ChatbotResponse { Answer = naturalAnswer, Books = recommendedBooks });
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

            // 1️⃣ PHÂN TÍCH TỪ KHÓA VÀ GIÁ
            var searchTerms = request.Message.ToLower().Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Where(t => t.Length > 2)
                                             .Distinct()
                                             .ToList();
            var minPrice = 0m;
            var maxPrice = decimal.MaxValue;
            bool priceSearched = false;

            foreach (var term in searchTerms)
            {
                if (decimal.TryParse(term, out var priceValue))
                {
                    minPrice = priceValue;
                    maxPrice = decimal.MaxValue;
                    priceSearched = true;
                    break;
                }
            }

            var booksQuery = _context.Books
                .Include(b => b.BookType)
                .Where(b => b.BookstoreId == request.BookstoreId)
                .AsExpandable();

            var predicate = PredicateBuilder.New<Book>(true);

            // Lọc theo Giá nếu có từ khóa giá
            if (priceSearched)
            {
                predicate = predicate.And(b => b.Price >= minPrice && b.Price <= maxPrice);
            }

            // Lọc theo Từ khóa (FUZZY SEARCH)
            if (searchTerms.Any())
            {
                var keywordPredicate = PredicateBuilder.New<Book>(false);

                foreach (var term in searchTerms.Take(5))
                {
                    var innerTerm = term;
                    keywordPredicate = keywordPredicate.Or(b =>
                        EF.Functions.ILike(b.Title, $"%{innerTerm}%") ||
                        (b.Author != null && EF.Functions.ILike(b.Author, $"%{innerTerm}%")) ||
                        (b.BookType != null && EF.Functions.ILike(b.BookType.Name, $"%{innerTerm}%")) ||
                        (b.Description != null && EF.Functions.ILike(b.Description, $"%{innerTerm}%"))
                    );
                }
                predicate = predicate.And(keywordPredicate);
            }

            var books = await booksQuery.Where(predicate)
                .OrderByDescending(b => b.AverageRating)
                .ThenByDescending(b => b.RatingsCount)
                .Take(10)
                .ToListAsync();

            // ⚠️ Logic Fallback: Nếu không tìm thấy sách nào khớp, lấy sách phổ biến nhất của cửa hàng đó
            if (!books.Any())
            {
                books = await _context.Books
                    .Include(b => b.BookType)
                    .Where(b => b.BookstoreId == request.BookstoreId)
                    .OrderByDescending(b => b.RatingsCount)
                    .Take(5)
                    .ToListAsync();

                if (!books.Any())
                    return Ok(new ChatbotResponse { Answer = "Xin lỗi, không tìm thấy sách nào trong cửa hàng này.", Books = new List<BookInfo>() });
            }

            // ⚠️ KHÔNG CẦN TẠO bookInfos TẠM THỜI NỮA. CHÚNG TA SẼ DỰA VÀO PHẢN HỒI CỦA AI.

            // 2️⃣ Tạo context chi tiết (dùng cho prompt)
            string contextData = $"Dữ liệu các sách trong cửa hàng {request.BookstoreId} liên quan đến yêu cầu:\n";
            foreach (var b in books)
            {
                contextData += $"- **{b.Title}** (ID: {b.Id}, Thể loại: {b.BookType?.Name ?? "Không rõ"})\n";
                contextData += $"  Tác giả: {b.Author ?? "Không rõ"}\n";
                contextData += $"  Giá: {b.Price:C}, Số lượng còn: {b.Quantity}\n";
            }

            // 3️⃣ Gửi request đến Gemini
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

    a. Bắt đầu bằng dòng: `----BOOKS_JSON_START----`
    b. Dữ liệu: Một mảng JSON của các đối tượng BookInfo (chỉ chứa Id, Title, BookstoreId). **Chỉ bao gồm các sách bạn đã đề cập hoặc tham khảo trong câu trả lời tự nhiên.**
    c. Kết thúc bằng dòng: `----BOOKS_JSON_END----`

Ví dụ về JSON Sách:
----BOOKS_JSON_START----
[
    {{ ""Id"": 101, ""Title"": ""Tên Sách Hay"", ""BookstoreId"": {request.BookstoreId} }},
    {{ ""Id"": 102, ""Title"": ""Sách Tiếp Theo"", ""BookstoreId"": {request.BookstoreId} }}
]
----BOOKS_JSON_END----

Hãy bắt đầu phản hồi của bạn ngay bây giờ.
";

            var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var response = await http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

            var json = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonNode.Parse(json);
            var rawResponseText = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            if (string.IsNullOrEmpty(rawResponseText))
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });

            // 4️⃣ LOGIC PHÂN TÁCH PHẢN HỒI (TÁCH TEXT VÀ JSON)
            const string startDelimiter = "----BOOKS_JSON_START----";
            const string endDelimiter = "----BOOKS_JSON_END----";
            string naturalAnswer = rawResponseText;
            List<BookInfo> recommendedBooks = new List<BookInfo>();

            // Tìm và trích xuất JSON
            int startIndex = rawResponseText.IndexOf(startDelimiter);
            int endIndex = rawResponseText.IndexOf(endDelimiter, startIndex + startDelimiter.Length);

            if (startIndex != -1 && endIndex != -1)
            {
                // Lấy phần văn bản trước JSON
                naturalAnswer = rawResponseText.Substring(0, startIndex).Trim();

                // Trích xuất chuỗi JSON
                string jsonPart = rawResponseText.Substring(startIndex + startDelimiter.Length, endIndex - (startIndex + startDelimiter.Length)).Trim();

                // Thử deserialize JSON
                try
                {
                    // Loại bỏ bất kỳ ký tự markdown nào mà AI có thể thêm vào
                    jsonPart = Regex.Replace(jsonPart, @"^```json\s*|```\s*$", string.Empty, RegexOptions.Multiline).Trim();

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var deserializedBooks = JsonSerializer.Deserialize<List<BookInfo>>(jsonPart, options);

                    if (deserializedBooks != null)
                    {
                        recommendedBooks = deserializedBooks;
                    }
                }
                catch (JsonException ex)
                {
                    // Log lỗi nếu JSON không hợp lệ, nhưng vẫn trả về câu trả lời tự nhiên
                    Console.WriteLine($"Lỗi phân tích JSON từ AI: {ex.Message}");
                }
            }

            // Trả về kết quả cho frontend, kèm theo danh sách sách đã được AI đề xuất
            return Ok(new ChatbotResponse { Answer = naturalAnswer, Books = recommendedBooks });
        }

        // --------------------------------------------------------------------------------------
        // MODELS
        // --------------------------------------------------------------------------------------

        public class StoreChatRequest
        {
            public string Message { get; set; } = string.Empty;
            public int BookstoreId { get; set; }
        }

        public class ChatbotResponse
        {
            public string Answer { get; set; } = string.Empty;
            public List<BookInfo>? Books { get; set; }
        }

        public class BookInfo
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("bookstoreId")]
            public int BookstoreId { get; set; }

            [JsonPropertyName("price")]
            public decimal Price { get; set; }

            [JsonPropertyName("imageUrl")]
            public string ImageUrl { get; set; } = string.Empty;
        }

        public class ChatRequest
        {
            public string Question { get; set; } = string.Empty;
        }
    }
}
