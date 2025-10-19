using AutoMapper;
using BookService.Application.Interface;
using BookService.Application.Models;
using BookService.Domain.Entities;
using BookService.Infracstructure.Repositories;
using Microsoft.AspNetCore.Http;
namespace BookService.Application.Services
{
    public class BookImageServices : IBookImageServices
    {
        private readonly BookImageRepository _repo;
        private readonly IMapper _mapper;

        private readonly ICloudinaryService _cloudinaryService;


        public BookImageServices(BookImageRepository repo, IMapper mapper, ICloudinaryService cloudinaryService)
        {
            _repo = repo;
            _mapper = mapper;
            _cloudinaryService = cloudinaryService;

        }

        public async Task<BookImage> CreateAsync(BookImageCreateRequest request)
        {
            string imageUrl = null;
            if (request.ImageFile != null)
            {
                imageUrl = await _cloudinaryService.UploadImageAsync(request.ImageFile);
            }

            var entity = _mapper.Map<BookImage>(request);
            entity.ImageUrl = imageUrl;
            entity.UploadedAt = DateTime.UtcNow;

            var created = await _repo.CreateAsync(entity);
            return created;
        }

        public async Task<BookImage> UpdateAsync(int id, IFormFile imageFile)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null)
                throw new Exception("BookImage not found");

            if (imageFile != null)
            {
                entity.ImageUrl = await _cloudinaryService.UploadImageAsync(imageFile);
                entity.UploadedAt = DateTime.UtcNow;
            }

            var updated = await _repo.UpdateAsync(entity);
            return updated;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null)
                return false;

            await _repo.DeleteAsync(entity);
            return true;
        }

        public async Task<IEnumerable<BookImage>> GetAllAsync()
        {
            return await _repo.GetAllAsync();
        }

        public async Task<BookImage> GetByBookIdAsync(int bookId)
        {
            return await _repo.GetByIdAsync(bookId);
        }
    }
}
