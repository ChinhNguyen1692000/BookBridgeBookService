using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BookService.Infracstructure.DBContext; // sửa namespace nếu khác

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

        [HttpGet("api/chatbot/ping")]
        public IActionResult Ping() => Ok("Chatbot API is alive!");

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question cannot be empty");

            // 1. Lấy dữ liệu thật từ DB (RAG)
            var books = await _context.Books
                .Include(b => b.BookType)
                .Take(5)
                .ToListAsync();

            string contextData = "Dữ liệu từ hệ thống BookBridge:\n";
            foreach (var b in books)
            {
                contextData += $"- {b.Title} ({b.BookType?.Name ?? "Không rõ thể loại"})\n";
            }

            // 2. Chuẩn bị request đến OpenRouter (miễn phí / open model)
            var http = _httpClientFactory.CreateClient();
            // http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config["OpenRouter:ApiKey"]}");
            // http.DefaultRequestHeaders.Add("User-Agent", "BookBridgeChatbot/1.0");
            // http.DefaultRequestHeaders.Add("HTTP-Referer", "https://bookbridgebookservice.onrender.com");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_config["OpenRouter:ApiKey"]}");
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BookBridgeChatbot/1.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bookbridgebookservice.onrender.com");

            http.DefaultRequestHeaders.Add("X-Title", "BookBridge Chatbot");

            // var body = new
            // {
            //     model = "meta-llama/llama-3-8b-instruct", // đổi model cho chắc chắn
            //     messages = new[]
            //     {
            //         new { role = "system", content = "Bạn là chatbot tư vấn hệ thống quản lý nhà sách BookBridge." },
            //         new { role = "user", content = $"{contextData}\n\nCâu hỏi: {request.Question}" }
            // }
            // };

            var body = new
            {
                model = "bookbridge-chatbot", // dùng slug của preset bạn tạo
                messages = new[]
                {
                    new { role = "user", content = $"{contextData}\n\nCâu hỏi: {request.Question}" }
                }
            };


            var response = await http.PostAsync(
                "https://openrouter.ai/api/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            );

            // var json = await response.Content.ReadAsStringAsync();
            // // return Content(json, "application/json");
            // if (!response.IsSuccessStatusCode)
            // {
            //     return StatusCode((int)response.StatusCode, new { error = json });
            // }

            var jsonDoc = JsonNode.Parse(json);
            var message = jsonDoc?["choices"]?[0]?["message"]?["content"]?.ToString();

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
