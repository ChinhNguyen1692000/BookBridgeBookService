using System; // Cần thêm using System cho Guid và DateTime
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookService.Domain.Entities
{
    [Table("ChatSessions")]
    public class ChatSession
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public Guid? UserId { get; set; }

        public int? BookstoreId { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime LastActive { get; set; }

        // Navigation Property: Lịch sử tin nhắn của phiên này
        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}