using BookService.Domain.Entities;
using BookService.Infracstructure.DBContext;
using Common.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookService.Infracstructure.Repositories
{
    public class BookRepository : BaseRepository<Book, int>
    {
        public BookRepository(BookDBContext context) : base(context) { }
        public async Task<bool> Remove(int id)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return !entity.IsActive;
        }
        public async Task<bool> Active(int id)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
            entity.IsActive = true;
            await _context.SaveChangesAsync();
            return entity.IsActive;
        }
        public async Task<List<Book>> GetActiveBookByBookstore(int id)
        {
            return await _dbSet.Include(b => b.BookType).Where(b => b.IsActive && b.BookstoreId == id).ToListAsync();
        }
        public async Task<List<Book>> GetUnactiveBookByBookStore(int id)
        {
            return await _dbSet.Include(b => b.BookType).Where(b => !b.IsActive && b.BookstoreId == id).ToListAsync();
        }
        public async Task<Book> GetByIdAsync(int id)
        {
            return await _dbSet.Include(b => b.BookType).Include(b => b.BookImages).FirstOrDefaultAsync(b => b.Id == id);
        }
        public async Task<List<Book>> Filter(int? typeId, decimal? price, string? searchValue)
        {
            var query = _dbSet.Include(b => b.BookType).Where(b => b.IsActive);

            if (typeId.HasValue)
                query = query.Where(b => b.TypeId == typeId);

            if (price.HasValue)
                query = query.Where(b => b.Price <= price.Value);

            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                var lower = searchValue.ToLower();
                query = query.Where(b =>
                    b.Author.ToLower().Contains(lower) ||
                    b.Title.ToLower().Contains(lower));
            }

            return await query.ToListAsync();
        }
        public async Task<List<Book>> GetAllBook()
        {
            return await _dbSet.Include(b => b.BookType).Where(b => b.IsActive).ToListAsync();
        }
        public async Task<List<Book>> SearchByTitleOrAuthor(string? searchValue)
        {
            var bL = _dbSet.Include(b => b.BookType).Where(b => b.IsActive).AsQueryable();
            if (!string.IsNullOrEmpty(searchValue))
            {
                bL = bL.Where(b => b.Title.ToLower().Contains(searchValue.ToLower()) || b.Author.ToLower().Contains(searchValue.ToLower()));
            }
            return await bL.ToListAsync();
        }


        public async Task<bool> BuyBook(int id, int quantity)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null)
            {
                return false;
            }
            if (entity.Quantity < quantity)
            {
                return false;
            }
            entity.Quantity -= quantity;
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> RefundBook(int id, int quantity)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null)
            {
                return false;
            }
            entity.Quantity += quantity;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
