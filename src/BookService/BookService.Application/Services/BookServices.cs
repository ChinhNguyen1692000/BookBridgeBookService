using AutoMapper;
using BookService.Application.Interface;
using BookService.Application.Models;
using BookService.Domain.Data;
using BookService.Domain.Entities;
using BookService.Infracstructure.Repositories;
using Common.Paging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BookService.Application.Services
{
    public class BookServices : IBookServices
    {
        private readonly BookRepository _repo;
        private readonly IMapper _mapper;
        private readonly ICloudinaryService _cloudinaryService;


        public BookServices(BookRepository repo, IMapper mapper, ICloudinaryService cloudinaryService)
        {
            _repo = repo;
            _mapper = mapper;
            _cloudinaryService = cloudinaryService;

        }
        public async Task<PagedResult<Book>> Search(string? searchValue, int pageNo = 1, int pageSize = 10)
        {
            var bL = await _repo.SearchByTitleOrAuthor(searchValue);
            var bLPaging = PagedResult<Book>.Create(bL, pageNo, pageSize);
            return bLPaging;
        }
        public async Task<PagedResult<Book>> Filter(BookFilterRequest request, int pageNo = 1, int pageSize = 10)
        {
            var bL = await _repo.Filter(request.TypeId, request.Price, request.SearchValue);
            var bLPaging = PagedResult<Book>.Create(bL, pageNo, pageSize);
            return bLPaging;
        }
        public async Task<PagedResult<Book>> GetAllAsync(int pageNo, int pageSize)
        {
            var bL = await _repo.GetAllBook();
            var bLPaging = PagedResult<Book>.Create(bL, pageNo, pageSize);
            return bLPaging;
        }

        public async Task<Book> GetByIdAsync(int id)
        {
            return await _repo.GetByIdAsync(id);
        }

        public async Task<Book> CreateAsync(BookCreateDTO dto)
        {
            var entity = new Book();
            _mapper.Map(dto, entity);
            entity.CreatedAt = DateTime.UtcNow;
            return await _repo.CreateAsync(entity);
        }

        public async Task<Book> UpdateAsync(BookUpdateDTO dto)
        {
            var exist = await _repo.GetByIdAsync(dto.Id);
            if (exist == null)
                throw new Exception("Book not found");

            _mapper.Map(dto, exist);
            exist.UpdatedAt = DateTime.UtcNow;

            return await _repo.UpdateAsync(exist);
        }

        public async Task<bool> Remove(int id)
        {
            return await _repo.Remove(id);
        }

        public async Task<bool> Active(int id)
        {
            return await _repo.Active(id);
        }

        public async Task<PagedResult<Book>> GetActiveByBookstoreAsync(int bookstoreId, int pageNo, int pageSize)
        {
            var bL = await _repo.GetActiveBookByBookstore(bookstoreId);
            var bLPaging = PagedResult<Book>.Create(bL, pageNo, pageSize);
            return bLPaging;
        }

        public async Task<PagedResult<Book>> GetInactiveByBookstoreAsync(int bookstoreId, int pageNo, int pageSize)
        {
            var bL = await _repo.GetUnactiveBookByBookStore(bookstoreId);
            var bLPaging = PagedResult<Book>.Create(bL, pageNo, pageSize);
            return bLPaging;
        }

        public async Task<bool> BuyBook(List<BookBuyRefund> request)
        {
            return await _repo.BuyBooksAsync(request);
        }
        public async Task<bool> RefundBook(List<BookBuyRefund> request)
        {
            return await _repo.RefundBooksAsync(request);
        }
    }
}
