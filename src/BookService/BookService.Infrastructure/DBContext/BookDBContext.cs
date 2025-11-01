using BookService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookService.Infracstructure.DBContext
{
    public class BookDBContext : DbContext
    {
        public BookDBContext(DbContextOptions<BookDBContext> options)
            : base(options)
        {
        }

        // DbSets
        public DbSet<Book> Books { get; set; }
        public DbSet<BookImage> BookImages { get; set; }
        public DbSet<BookType> BookTypes { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<ChatSession> ChatSessions { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ChatSession
            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.CreatedAt)
                    .HasColumnType("timestamptz")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.LastActive)
                    .HasColumnType("timestamptz")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasMany(s => s.Messages)
                      .WithOne(m => m.Session)
                      .HasForeignKey(m => m.SessionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ChatMessage
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Sender)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(e => e.Content)
                      .IsRequired()
                      .HasColumnType("text");

                entity.Property(e => e.Timestamp)
                      .HasColumnType("timestamptz")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Book
            modelBuilder.Entity<Book>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Title)
                      .IsRequired()
                      .HasMaxLength(255);

                entity.Property(e => e.ISBN)
                      .IsRequired()
                      .HasMaxLength(20);

                entity.Property(e => e.ImageUrl)
                      .IsRequired()
                      .HasMaxLength(500);

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired(false);

                entity.Property(e => e.IsActive)
                      .HasDefaultValue(true);

                entity.Property(e => e.Price)
                      .HasColumnType("numeric(18,2)")
                      .IsRequired();

                entity.HasOne(b => b.BookType)
                      .WithMany()
                      .HasForeignKey(b => b.TypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(b => b.BookImages)
                      .WithOne(bi => bi.Book)
                      .HasForeignKey(bi => bi.BookId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // BookImage
            modelBuilder.Entity<BookImage>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ImageUrl)
                      .IsRequired()
                      .HasMaxLength(500);

                entity.Property(e => e.UploadedAt)
                      .HasColumnType("timestamptz")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // BookType
            modelBuilder.Entity<BookType>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Description)
                      .HasMaxLength(255);

                entity.Property(e => e.isActive)
                      .HasDefaultValue(true);
            });

            // Message
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.EventType)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Status)
                      .HasMaxLength(50)
                      .HasDefaultValue("Pending");

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.PublishedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired(false);

                entity.Property(e => e.TraceId)
                      .HasMaxLength(255);
            });
        }
    }
}
