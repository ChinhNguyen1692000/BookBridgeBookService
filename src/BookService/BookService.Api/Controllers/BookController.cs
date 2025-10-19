using BookService.Application.Interface;
using BookService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace BookService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookController : ControllerBase
    {
        private readonly IBookServices _service;
        private readonly ICloudinaryService _cloudinaryService;

        public BookController(IBookServices service, ICloudinaryService cloudinaryService)
        {
            _service = service;
            _cloudinaryService = cloudinaryService;
        }
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int pageNo = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _service.GetAllAsync(pageNo, pageSize);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var book = await _service.GetByIdAsync(id);
            if (book == null) return NotFound();
            return Ok(book);
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] BookCreateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            string? imageUrl = null;
            if (request.ImageFile != null)
                imageUrl = await _cloudinaryService.UploadImageAsync(request.ImageFile);

            var dto = new BookCreateDTO
            {
                ISBN = request.ISBN,
                BookstoreId = request.BookstoreId,
                Title = request.Title,
                Author = request.Author,
                Translator = request.Translator,
                Quantity = request.Quantity,
                Publisher = request.Publisher,
                PublishedDate = request.PublishedDate,
                Price = request.Price,
                Language = request.Language,
                Description = request.Description,
                PageCount = request.PageCount,
                TypeId = request.TypeId,
                ImageUrl = imageUrl
            };

            var created = await _service.CreateAsync(dto);
            return Ok(created);
        }


        [HttpPut]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update([FromForm] BookUpdateRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            string? imageUrl = null;
            if (request.ImageFile != null)
                imageUrl = await _cloudinaryService.UploadImageAsync(request.ImageFile);

            var dto = new BookUpdateDTO
            {
                Id = request.Id,
                ISBN = request.ISBN,
                BookstoreId = request.BookstoreId,
                Title = request.Title,
                Author = request.Author,
                Translator = request.Translator,
                Quantity = request.Quantity,
                Publisher = request.Publisher,
                PublishedDate = request.PublishedDate,
                Price = request.Price,
                Language = request.Language,
                Description = request.Description,
                PageCount = request.PageCount,
                TypeId = request.TypeId,
                ImageUrl = imageUrl
            };

            try
            {
                var updated = await _service.UpdateAsync(dto);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Remove(int id)
        {
            var result = await _service.Remove(id);
            if (!result) return NotFound();
            return Ok(new { message = "Book deactivated successfully" });
        }

        [HttpPut("active/{id}")]
        public async Task<IActionResult> Active(int id)
        {
            var result = await _service.Active(id);
            if (!result) return NotFound();
            return Ok(new { message = "Book activated successfully" });
        }

        [HttpGet("active/{bookstoreId}")]
        public async Task<IActionResult> GetActiveByBookstore(int bookstoreId, int pageNo = 1, int pageSize = 10)
        {
            var result = await _service.GetActiveByBookstoreAsync(bookstoreId, pageNo, pageSize);
            return Ok(result);
        }

        [HttpGet("inactive/{bookstoreId}")]
        public async Task<IActionResult> GetInactiveByBookstore(int bookstoreId, int pageNo = 1, int pageSize = 10)
        {
            var result = await _service.GetInactiveByBookstoreAsync(bookstoreId, pageNo, pageSize);
            return Ok(result);
        }
        [HttpGet("filter")]
        public async Task<IActionResult> Filter([FromQuery] BookFilterRequest request, [FromQuery] int pageNo = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var result = await _service.Filter(request, pageNo, pageSize);

                if (result == null)
                    return NotFound("No matching book found");

                return Ok(result);
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }
        }
        [HttpGet("search")]
        public async Task<IActionResult> Search(string searchValue, int pageNo = 1, int pageSize = 10)
        {
            try
            {
                var result = await _service.Search(searchValue, pageNo, pageSize);
                if (result == null)
                {
                    return NotFound("No matching book found");
                }
                return Ok(result);
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }
        }

    }
}
