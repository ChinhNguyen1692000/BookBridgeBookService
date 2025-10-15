using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace BookService.Infracstructure.DBContext
{
    public class BookDBContextFactory : IDesignTimeDbContextFactory<BookDBContext>
    {
        public BookDBContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<BookDBContext>();
            var connectionString = configuration.GetConnectionString("BookServiceConnection"); // đổi theo tên connection string của bạn
            optionsBuilder.UseNpgsql(connectionString); // dùng PostgreSQL

            return new BookDBContext(optionsBuilder.Options);
        }
    }
}
