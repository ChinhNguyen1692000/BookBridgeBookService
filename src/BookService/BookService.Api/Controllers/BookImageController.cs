using BookService.Application.Services;
using BookService.Application.Models;
using BookService.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BookService.Application.Interface;

namespace BookService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookImageController : ControllerBase
    {
        private readonly IBookImageServices _service;

        public BookImageController(IBookImageServices service)
        {
            _service = service;
        }

        // GET: api/BookImage
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var images = await _service.GetAllAsync();
            return Ok(images);
        }

        // GET: api/BookImage/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var image = await _service.GetByBookIdAsync(id);
            if (image == null) return NotFound();
            return Ok(image);
        }

        // POST: api/BookImage
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] BookImageCreateRequest request)
        {
            if (request.ImageFile == null)
                return BadRequest("Image file is required.");

            var created = await _service.CreateAsync(request);
            return Ok(created);
        }

        // PUT: api/BookImage/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromForm] IFormFile imageFile)
        {
            if (imageFile == null)
                return BadRequest("Image file is required.");

            try
            {
                var updated = await _service.UpdateAsync(id, imageFile);
                return Ok(updated);
            }
            catch (System.Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // DELETE: api/BookImage/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _service.DeleteAsync(id);
            if (!deleted) return NotFound();
            return NoContent();
        }
    }
}
