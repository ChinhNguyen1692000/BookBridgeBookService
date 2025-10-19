using BookService.Application.Models;
using BookService.Domain.Data;
using BookService.Domain.Entities;
using Common.Paging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookService.Application.Interface
{
    public interface IBookServices
    {
        Task<bool> BuyBook(List<BookBuyRefund> request);
        Task<bool> RefundBook(List<BookBuyRefund> request);
        Task<PagedResult<Book>> Search(string? searchValue, int pageNo = 1, int pageSize = 10);
        Task<PagedResult<Book>> Filter(BookFilterRequest request, int pageNo = 1, int pageSize = 10);
        Task<PagedResult<Book>> GetAllAsync(int pageNo, int pageSize);

        Task<Book> GetByIdAsync(int id);

        Task<Book> CreateAsync(BookCreateDTO dto);
        Task<Book> UpdateAsync(BookUpdateDTO dto);

        Task<bool> Remove(int id);
        Task<bool> Active(int id);

        Task<PagedResult<Book>> GetActiveByBookstoreAsync(int bookstoreId, int pageNo, int pageSize);

        Task<PagedResult<Book>> GetInactiveByBookstoreAsync(int bookstoreId, int pageNo, int pageSize);
    }
}
