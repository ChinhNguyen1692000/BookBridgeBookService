using System; // Cần thêm using System cho DateTime
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization; // Thêm cho [JsonIgnore]

namespace BookService.Domain.Entities
{
    [Table("ChatMessages")]
    public class ChatMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [ForeignKey("Session")] // Khai báo khóa ngoại
        public int SessionId { get; set; }

        [Required] // Nên thêm Required cho Sender
        [MaxLength(50)] // Giới hạn độ dài cho Sender ('User' hoặc 'AI')
        public string Sender { get; set; }

        [Required] // Nên thêm Required cho Content
        [Column(TypeName = "text")] // Sử dụng kiểu TEXT cho nội dung dài
        public string Content { get; set; }

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow; // Gán giá trị mặc định

        // Navigation Property
        [JsonIgnore]
        public virtual ChatSession Session { get; set; } // Liên kết đến phiên trò chuyện
    }
}