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
using System.Linq; // Thêm thư viện Linq

namespace BookService.Api.Controllers
{
    // --- DTOs ĐẦU VÀO VÀ ĐẦU RA ---
    public class ChatRequest
    {
        public string Question { get; set; }
    }

    public class BookInfo
    {
        public int Id { get; set; } // GIỮ LẠI ID cho việc tái truy vấn
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
        //                         HÀM HỖ TRỢ NGỮ CẢNH
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
                session.LastActive = DateTime.UtcNow;
                _context.ChatSessions.Update(session);
            }
            // Nếu không tìm thấy (hoặc là guest), tạo session mới
            else
            {
                session = new BookService.Domain.Entities.ChatSession
                {
                    UserId = userId, // Sẽ là null nếu là guest
                    BookstoreId = bookstoreId,
                    CreatedAt = DateTime.UtcNow,
                    LastActive = DateTime.UtcNow
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
                Timestamp = DateTime.UtcNow
            };
            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();
        }

        // =========================================================================
        //                                 ENDPOINTS
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

            // LOGIC TÌM KIẾM NÂNG CAO VÀ TỪ KHÓA

            // Tách từ khóa tìm kiếm cơ bản (Giữ nguyên)
            var searchTerms = request.Question.ToLower().Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Where(t => t.Length > 2)
                                                 .Distinct()
                                                 .Take(5)
                                                 .ToList();

            // KHỞI TẠO PREDICATE CHO TÌM KIẾM NÂNG CAO/CƠ BẢN
            var booksQuery = _context.Books.Include(b => b.BookType).AsExpandable();
            var predicate = PredicateBuilder.New<Book>(true);

            // 1. LỌC CƠ BẢN: PHẢI ACTIVE VÀ CÓ SỐ LƯỢNG TỒN KHO
            predicate = predicate.And(b => b.IsActive == true && b.Quantity > 0);

            // 2. LỌC THEO TỪ KHÓA (nếu có)
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
                        (b.Description != null && EF.Functions.ILike(b.Description, $"%{innerTerm}%")) ||
                        (b.Publisher != null && EF.Functions.ILike(b.Publisher, $"%{innerTerm}%"))); // Thêm Publisher vào tìm kiếm từ khóa
                }
                predicate = predicate.And(keywordPredicate);
            }

            // 3. THỰC HIỆN TRUY VẤN VÀ SẮP XẾP THEO YÊU CẦU MỚI (Rating/Count/Date)
            // **Lưu ý**: Nếu KHÔNG có từ khóa tìm kiếm, ta ưu tiên sắp xếp theo các tiêu chí mới.
            if (!searchTerms.Any())
            {
                // Ưu tiên đề xuất theo đánh giá/lượt đánh giá, hoặc sách mới nhất nếu không có tiêu chí rõ ràng
                predicate = predicate.And(b => b.AverageRating.HasValue && b.RatingsCount.HasValue && b.RatingsCount.Value > 0); // Chỉ xem xét sách đã có đánh giá
                booksQuery = booksQuery.OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.RatingsCount);
            }
            else
            {
                // Nếu có từ khóa, sắp xếp theo mức độ liên quan (giữ nguyên)
                booksQuery = booksQuery.OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.RatingsCount);
            }


            var books = await booksQuery.Where(predicate).Take(10).ToListAsync();

            // Xử lý trường hợp không tìm thấy sách theo từ khóa (nếu có) hoặc tiêu chí rating
            if (!books.Any() && searchTerms.Any())
            {
                // Nếu tìm kiếm từ khóa không ra, quay về đề xuất sách tốt nhất/mới nhất (vẫn phải active & quantity > 0)
                books = await _context.Books
                    .Include(b => b.BookType)
                    .Where(b => b.IsActive == true && b.Quantity > 0)
                    .OrderByDescending(b => b.PublishedDate ?? DateTime.MinValue) // Ưu tiên sách mới nhất
                    .ThenByDescending(b => b.AverageRating ?? 0)
                    .Take(10)
                    .ToListAsync();
            }
            else if (!books.Any() && !searchTerms.Any())
            {
                // Nếu không có từ khóa và không có sách nào đủ điều kiện rating/count, đề xuất chung (active & quantity > 0)
                books = await _context.Books
                    .Include(b => b.BookType)
                    .Where(b => b.IsActive == true && b.Quantity > 0)
                    .OrderByDescending(b => b.AverageRating ?? 0)
                    .Take(10)
                    .ToListAsync();
            }

            // -------------------------------------------------

            // TẠO CONTEXT DỮ LIỆU SÁCH CHO GEMINI (Bao gồm các trường mới)
            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books) {
                contextData += $"- **{b.Title}**\n";
                contextData += $"  * **ID**: {b.Id}\n";
                contextData += $"  * **Mô tả (Description)**: {b.Description?.Substring(0, Math.Min(b.Description.Length, 100)) + "..." ?? "Không có"}\n"; // Mô tả ngắn
                contextData += $"  * **Tác giả (Author)**: {b.Author ?? "Không rõ"}\n";
                contextData += $"  * **Nhà xuất bản (Publisher)**: {b.Publisher ?? "Không rõ"}\n";
                contextData += $"  * **Ngôn ngữ (Language)**: {b.Language ?? "Không rõ"}\n";
                contextData += $"  * **Ngày xuất bản (PublishedDate)**: {b.PublishedDate?.ToString("yyyy-MM-dd") ?? "Không rõ"}\n";
                contextData += $"  * **Đánh giá (AverageRating)**: {b.AverageRating?.ToString("F1") ?? "Chưa có"} / 5.0 ({b.RatingsCount ?? 0} lượt)\n";
                contextData += $"  * **Tồn kho (Quantity)**: {b.Quantity}\n"; // Đã check Quantity > 0 ở query
                contextData += $"  * **Giá**: {b.Price:C}\n";
            }
            // -------------------------------------------------

            // Chuẩn bị request đến Gemini API (ĐÃ CẬP NHẬT PROMPT)
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
2. **Sử dụng Dữ liệu Cụ thể & Linh hoạt Rating (ĐÃ SỬA):** * **Ưu tiên:** Chỉ trích dẫn điểm số và lượt đánh giá (**AverageRating** và **RatingsCount**) nếu câu hỏi của người dùng **có đề cập hoặc ám chỉ đến chất lượng/độ nổi tiếng/lượt mua/số lượng đánh giá**. 
    * **Nếu không có yêu cầu cụ thể:** Chỉ cần đề xuất tên sách và mô tả ngắn. 
    * **Cú pháp:** Trích dẫn điểm số (vd: ""sách có rating 4.5/5.0 với 120 lượt đánh giá"").
3. **Tính ngẫu nhiên & Giới hạn:** Đảm bảo câu trả lời **có sự ngẫu nhiên**. Nếu người dùng yêu cầu nhiều hơn 10 cuốn, hãy giải thích rằng bạn chỉ có thể đề xuất tối đa **10 cuốn mỗi lần**.
4. **Trò chuyện:** Nếu câu hỏi chỉ mang tính trò chuyện, **không cần** đề xuất sách và trả về mảng JSON sách rỗng (`[]`).
5. **Định dạng Sách (MARKDOWN - ĐÃ SỬA):** Khi nhắc đến sách, hãy dùng cú pháp Markdown: `**Tên Sách**`. **Tuyệt đối KHÔNG** thêm ID vào tên sách trong phần trả lời (ví dụ: không dùng `**Tên Sách (ID: 123)**`).
6. **Dữ liệu JSON (BẮT BUỘC):** Luôn đính kèm danh sách sách bạn tham chiếu vào cuối phản hồi theo định dạng JSON sau:
    a. Bắt đầu bằng dòng: `----BOOKS_JSON_START----`
    b. Dữ liệu: Một mảng JSON của các đối tượng BookInfo (**chỉ chứa Id, Title, BookstoreId**). Nếu bạn **không đề xuất bất kỳ cuốn sách nào** trong câu trả lời (chỉ trò chuyện), hãy trả về mảng rỗng `[]`.
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
                await SaveChatMessage(sessionId, "AI", "Xin lỗi, tôi đang gặp sự cố khi kết nối với bộ não AI.");
                return StatusCode(500, new { Answer = "Xin lỗi, tôi đang gặp sự cố khi kết nối với bộ não AI.", Books = new List<BookInfo>(), SessionId = sessionId });
            }

            // LOGIC PHÂN TÁCH PHẢN HỒI
            const string startDelimiter = "----BOOKS_JSON_START----";
            const string endDelimiter = "----BOOKS_JSON_END----";
            string naturalAnswer = rawResponseText;
            List<BookInfo> recommendedBooksIdsFromAI = new List<BookInfo>(); // Dùng để lưu Id/Title/BookstoreId từ AI
            bool aiRequestedBookData = false;

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
                    
                    if (deserializedBooks != null)
                    {
                         recommendedBooksIdsFromAI = deserializedBooks.Where(b => b.Id > 0).ToList(); // Chỉ lấy sách có ID hợp lệ
                         // Đánh dấu là AI đã xử lý JSON, kể cả khi JSON rỗng
                         aiRequestedBookData = true; 
                    }
                }
                catch (JsonException ex) { 
                    Console.WriteLine($"Lỗi phân tích JSON từ AI: {ex.Message}"); 
                    // Nếu lỗi JSON, vẫn tiếp tục với logic ở bước 3
                }
            }
            // -------------------------------------------------

            List<BookInfo> finalRecommendedBooks = new List<BookInfo>();

            // 1. TÁI TRUY VẤN DATABASE DỰA TRÊN ID TỪ AI (Nếu có)
            if (recommendedBooksIdsFromAI.Any())
            {
                var recommendedBookIds = recommendedBooksIdsFromAI.Select(b => b.Id).Distinct().ToList();
                
                var fullBookDetails = await _context.Books
                    .Where(b => recommendedBookIds.Contains(b.Id))
                    .Select(b => new BookInfo { 
                        Id = b.Id, 
                        Title = b.Title, 
                        BookstoreId = b.BookstoreId, 
                        Price = b.Price, 
                        ImageUrl = b.ImageUrl 
                    })
                    .ToListAsync();
                
                // Sắp xếp lại theo thứ tự AI yêu cầu (dựa trên ID trả về từ AI)
                finalRecommendedBooks = recommendedBookIds
                    .Select(id => fullBookDetails.FirstOrDefault(b => b.Id == id))
                    .Where(b => b != null)
                    .ToList()!;
            }
            // 2. LOGIC BÙ TRỪ: Nếu AI không cung cấp JSON sách (hoặc JSON bị lỗi/rỗng) 
            //    NHƯNG ta CÓ danh sách 'books' đã truy vấn từ DB (nghĩa là có sách để đề xuất), 
            //    ta sẽ sử dụng danh sách này để điền vào mảng trả về.
            else if (!aiRequestedBookData && books.Any())
            {
                 // Lấy các trường cần thiết từ list 'books' đã có để trả về cho client
                 finalRecommendedBooks = books
                    .Select(b => new BookInfo {
                         Id = b.Id,
                         Title = b.Title,
                         BookstoreId = b.BookstoreId,
                         Price = b.Price,
                         ImageUrl = b.ImageUrl
                    })
                    .ToList();
            }
            // -------------------------------------------------

            // LƯU TRỮ TIN NHẮN MỚI
            await SaveChatMessage(sessionId, $"{userName} ({userRole})", request.Question);
            await SaveChatMessage(sessionId, "AI", naturalAnswer);
            // -------------------------------------------------

            // Trả về kết quả cho client
            return Ok(new ChatbotResponse { 
                Answer = naturalAnswer, 
                Books = finalRecommendedBooks, // Sẽ có dữ liệu nếu có sách được đề xuất (kể cả khi AI quên gửi JSON)
                SessionId = sessionId 
            });
        }
    }
}