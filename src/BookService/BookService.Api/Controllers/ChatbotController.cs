using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookService.Infracstructure.DBContext;
using BookService.Domain.Entities;
using LinqKit;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt; 


namespace BookService.Api.Controllers
{
    // --- DTOs ĐẦU VÀO VÀ ĐẦU RA ---
    public class ChatRequest
    {
        // SessionId đã cmt
        public string Question { get; set; }
    }

    public class BookInfo
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int BookstoreId { get; set; }
        public decimal Price { get; set; }
        public string ImageUrl { get; set; }
    }

    public class ChatbotResponse
    {
        public string Answer { get; set; }
        public List<BookInfo> Books { get; set; }
        public int SessionId { get; set; } // Vẫn trả về SessionId cho client
    }
    // -------------------------------------------------------------------


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

        [HttpGet("/api/healthz")] // <-- Render check health
        public IActionResult HealthCheck() => Ok("Healthy");

        [HttpGet("ping")] // <-- Check health for chatbot
        public IActionResult Ping() => Ok("Chatbot API is alive!");

        // =========================================================================
        //                         HÀM HỖ TRỢ NGỮ CẢNH
        // =========================================================================

        private bool ShouldLoadFullHistory(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;
            var normalizedQuestion = question.ToLowerInvariant();
            var keywords = new[] { "nhớ", "lúc trước", "quá khứ", "tổng kết", "tóm tắt", "trước đây", "hôm qua", "lần trước", "toàn bộ" };
            return keywords.Any(keyword => normalizedQuestion.Contains(keyword));
        }

        // Tìm session gần nhất của User hoặc tạo session mới (cho user hoặc guest).
        private async Task<int> GetOrCreateSessionId(Guid? userId = null, int? bookstoreId = null)
        {
            BookService.Domain.Entities.ChatSession session = null;

            // Nếu là User đăng nhập, cố gắng tìm session gần nhất
            if (userId.HasValue)
            {
                session = await _context.ChatSessions
                    .Where(s => s.UserId == userId.Value)
                    .OrderByDescending(s => s.LastActive) // Lấy session hoạt động gần nhất
                    .FirstOrDefaultAsync();
            }

            // Nếu tìm thấy session của user, cập nhật nó
            if (session != null)
            {
                session.LastActive = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                _context.ChatSessions.Update(session);
            }
            // Nếu không tìm thấy (hoặc là guest), tạo session mới
            else
            {
                session = new BookService.Domain.Entities.ChatSession
                {
                    UserId = userId, // Sẽ là null nếu là guest
                    BookstoreId = bookstoreId,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    LastActive = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                _context.ChatSessions.Add(session);
            }

            await _context.SaveChangesAsync();
            return session.Id; // Trả về ID của session tìm thấy hoặc vừa tạo
        }

        // Tải lịch sử tin nhắn của Session.
        private async Task<string> LoadChatHistory(int sessionId, bool loadAll = false)
        {
            var query = _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.Timestamp);

            var historyQuery = loadAll ? query : query.Take(10);
            var history = await historyQuery.ToListAsync();
            history.Reverse();

            var sb = new StringBuilder();
            if (loadAll) sb.AppendLine("Toàn bộ lịch sử trò chuyện trong phiên này:");
            else sb.AppendLine("Lịch sử 5 tin nhắn gần nhất trong phiên này:");

            if (!history.Any())
            {
                sb.AppendLine("Không có lịch sử trò chuyện.");
            }
            else
            {
                foreach (var msg in history)
                {
                    string content = msg.Content.Replace("\n", " ").Trim();
                    sb.AppendLine($"[{msg.Sender}]: {content}");
                }
            }
            return sb.ToString();
        }


        // Lưu tin nhắn mới vào database.
        private async Task SaveChatMessage(int sessionId, string sender, string content)
        {
            var message = new BookService.Domain.Entities.ChatMessage
            {
                SessionId = sessionId,
                Sender = sender,
                Content = content,
                Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };
            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();
        }

        // =========================================================================
        //                                 ENDPOINTS
        // =========================================================================

        // [Authorize] // <-- Test xong thì bật lại
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request) // sessionId đã bị xóa khỏi đây
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // 1. TRÍCH XUẤT THÔNG TIN
            Guid? userId = null;
            string userRole = "Guest";
            string userName = "Bạn";

            if (User.Identity.IsAuthenticated)
            {
                var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.NameId);
                var roleClaim = User.FindFirstValue("role");
                var userNameClaim = User.FindFirstValue("name"); // Hoặc "unique_name"

                if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedGuid)) userId = parsedGuid;
                if (!string.IsNullOrEmpty(roleClaim)) userRole = roleClaim;
                if (!string.IsNullOrEmpty(userNameClaim)) userName = userNameClaim;
            }

            // 2. Tải lịch sử chat (Động)
            bool loadFullHistory = ShouldLoadFullHistory(request.Question);
            // Lấy hoặc tạo session DỰA TRÊN USERID (hoặc tạo mới cho guest)
            int sessionId = await GetOrCreateSessionId(userId: userId);
            string chatHistoryContext = await LoadChatHistory(sessionId, loadFullHistory);
            // -------------------------------------------------

            // LOGIC TÌM KIẾM THEO TỪ KHÓA (Giữ nguyên)
            var searchTerms = request.Question.ToLower().Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Where(t => t.Length > 2)
                                    .Distinct()
                                    .Take(5)
                                    .ToList();
            var booksQuery = _context.Books.Include(b => b.BookType).AsExpandable();
            var predicate = PredicateBuilder.New<Book>(true);
            if (searchTerms.Any())
            {
                var keywordPredicate = PredicateBuilder.New<Book>(false);
                foreach (var term in searchTerms)
                {
                    var innerTerm = term;
                    keywordPredicate = keywordPredicate.Or(b => EF.Functions.ILike(b.Title, $"%{innerTerm}%") || (b.Author != null && EF.Functions.ILike(b.Author, $"%{innerTerm}%")) || (b.BookType != null && EF.Functions.ILike(b.BookType.Name, $"%{innerTerm}%")) || (b.Description != null && EF.Functions.ILike(b.Description, $"%{innerTerm}%")));
                }
                predicate = predicate.And(keywordPredicate);
            }
            var books = await booksQuery.Where(predicate).OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.RatingsCount).Take(10).ToListAsync();
            if (!books.Any() && searchTerms.Any() == false)
            {
                books = await _context.Books.Include(b => b.BookType).OrderByDescending(b => b.RatingsCount).Take(10).ToListAsync();
            }
            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books) { contextData += $"- {b.Title} (ID: {b.Id}, {b.BookstoreId}, Thể loại: {b.BookType?.Name ?? "Không rõ"})\n  Tác giả: {b.Author ?? "Không rõ"}\n  Giá: {b.Price:C}\n"; }
            // -------------------------------------------------

            // Chuẩn bị request đến Gemini API (Giữ nguyên, có yêu cầu Markdown)
            var http = _httpClientFactory.CreateClient();
            var apiKey = _config["Gemini:ApiKey"];
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservicev2.onrender.com");
            var prompt = $@"
Bạn là một nữ trợ lý thông minh về sách.
Hãy trả lời câu hỏi của người dùng dựa trên ngữ cảnh sách được cung cấp dưới đây.

--- THÔNG TIN NGƯỜI DÙNG HIỆN TẠI ---
Người dùng này là {userName} với vai trò {userRole}.
--- END THÔNG TIN ---

--- LỊCH SỬ TRÒ CHUYỆN ---
{chatHistoryContext}
--- END LỊCH SỬ ---

--- CONTEXT DỮ LIỆU SÁCH ---
{contextData}
--- END CONTEXT ---

Người dùng hỏi: {request.Question}

--- HƯỚNG DẪN TRẢ LỜI ---
1. **Phản hồi (BẮT BUỘC MARKDOWN):** Trả lời một cách **nữ tính hơi cá tính**, hữu ích và lịch sự, sử dụng tiếng Việt. **TOÀN BỘ PHẦN TRẢ LỜI CỦA BẠN PHẢI ĐƯỢC ĐỊNH DẠNG BẰNG MARKDOWN.**
2. **Tính ngẫu nhiên & Giới hạn:** Đảm bảo câu trả lời **có sự ngẫu nhiên**. Nếu người dùng yêu cầu nhiều hơn 10 cuốn, hãy giải thích rằng bạn chỉ có thể đề xuất tối đa **10 cuốn mỗi lần**.
3. **Trò chuyện:** Nếu câu hỏi chỉ mang tính trò chuyện, **không cần** đề xuất sách và trả về mảng JSON sách rỗng ([]).
4. **Định dạng Sách (MARKDOWN):** Khi nhắc đến sách, hãy dùng cú pháp Markdown: `**Tên Sách [ID]**`.
5. **Dữ liệu JSON (BẮT BUỘC):** Luôn đính kèm danh sách sách bạn tham chiếu vào cuối phản hồi theo định dạng JSON sau:
    a. Bắt đầu bằng dòng: `----BOOKS_JSON_START----`
    b. Dữ liệu: Một mảng JSON của các đối tượng BookInfo (**chỉ chứa Id, Title, BookstoreId**). Có thể là mảng rỗng `[]`.
    c. Kết thúc bằng dòng: `----BOOKS_JSON_END----`

Hãy bắt đầu phản hồi của bạn ngay bây giờ.";
            var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var response = await http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonNode.Parse(json);
            var rawResponseText = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            // -------------------------------------------------

            if (string.IsNullOrEmpty(rawResponseText))
            {
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });
            }

            // LOGIC PHÂN TÁCH PHẢN HỒI
            const string startDelimiter = "----BOOKS_JSON_START----";
            const string endDelimiter = "----BOOKS_JSON_END----";
            string naturalAnswer = rawResponseText;
            List<BookInfo> recommendedBooks = new List<BookInfo>();
            int startIndex = rawResponseText.IndexOf(startDelimiter);
            int endIndex = rawResponseText.IndexOf(endDelimiter, startIndex + startDelimiter.Length);
            if (startIndex != -1 && endIndex != -1)
            {
                naturalAnswer = rawResponseText.Substring(0, startIndex).Trim();
                string jsonPart = rawResponseText.Substring(startIndex + startDelimiter.Length, endIndex - (startIndex + startDelimiter.Length)).Trim();
                try
                {
                    jsonPart = Regex.Replace(jsonPart, @"^```json\s*|```\s*$", string.Empty, RegexOptions.Multiline).Trim();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var deserializedBooks = JsonSerializer.Deserialize<List<BookInfo>>(jsonPart, options);
                    if (deserializedBooks != null) recommendedBooks = deserializedBooks;
                }
                catch (JsonException ex) { Console.WriteLine($"Lỗi phân tích JSON từ AI: {ex.Message}"); }
            }
            // -------------------------------------------------

            // TÁI TRUY VẤN DATABASE
            if (recommendedBooks.Any())
            {
                var recommendedBookIds = recommendedBooks.Select(b => b.Id).ToList();
                var fullBookDetails = await _context.Books.Where(b => recommendedBookIds.Contains(b.Id)).Select(b => new BookInfo { Id = b.Id, Title = b.Title, BookstoreId = b.BookstoreId, Price = b.Price, ImageUrl = b.ImageUrl }).ToListAsync();
                recommendedBooks = fullBookDetails;
            } else if (recommendedBooks.Count == 0 && startIndex != -1) {} else { recommendedBooks = new List<BookInfo>(); }
            // -------------------------------------------------

            // LƯU TRỮ TIN NHẮN MỚI
            await SaveChatMessage(sessionId, $"{userName} ({userRole})", request.Question);
            await SaveChatMessage(sessionId, "AI", naturalAnswer);
            // -------------------------------------------------

            // Trả về kết quả cho client
            return Ok(new ChatbotResponse { Answer = naturalAnswer, Books = recommendedBooks, SessionId = sessionId });
        }
    }
}