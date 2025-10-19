using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BookService.Infracstructure.DBContext;
using System.Text.Json.Nodes;

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

            // 1. Lấy dữ liệu từ DB (RAG)
            var books = await _context.Books
                .Include(b => b.BookType)
                .Take(5)
                .ToListAsync();

            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} ({b.BookType?.Name ?? "Không rõ thể loại"})\n";
            }

            // 2. Chuẩn bị request đến Gemini API
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", _config["Gemini:ApiKey"]);
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
                            new { text = $"{contextData}\n\nCâu hỏi: {request.Question}" }
                        }
                    }
                }
            };

            var response = await http.PostAsync(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            );

            var json = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonNode.Parse(json);
            var message = jsonDoc?["results"]?[0]?["content"]?.ToString();

            if (string.IsNullOrEmpty(message))
            {
                return StatusCode(500, new { error = "Không nhận được phản hồi từ AI." });
            }

            return Ok(new { answer = message });
        }
    }

    public class ChatRequest
    {
        public string Question { get; set; }
    }
}
