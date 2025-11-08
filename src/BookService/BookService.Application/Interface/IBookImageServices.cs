using BookService.Application.Models;
using BookService.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookService.Application.Interface
{
    public interface IBookImageServices
    {
        Task<BookImage> CreateAsync(BookImageCreateRequest request);

        Task<BookImage> UpdateAsync(int id, Microsoft.AspNetCore.Http.IFormFile imageFile);

        Task<bool> DeleteAsync(int id);

        Task<IEnumerable<BookImage>> GetAllAsync();
        Task<List<BookImage>> GetByBookIdAsync(int bookId);
    }
}
