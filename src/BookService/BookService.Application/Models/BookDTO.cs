using BookService.Domain.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BookService.Application.Models
{
    public class BookCreateRequest
    {
        public string ISBN { get; set; }
        public int BookstoreId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Translator { get; set; }
        public int Quantity { get; set; }

        public string Publisher { get; set; }
        public DateTime? PublishedDate { get; set; }
        public decimal Price { get; set; }
        public string Language { get; set; }
        public string Description { get; set; }
        public int? PageCount { get; set; }
        public int TypeId { get; set; }

        // Ảnh bìa
        public IFormFile? ImageFile { get; set; }
    }
    public class BookCreateDTO
    {
        public string ISBN { get; set; }
        public int BookstoreId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Translator { get; set; }
        public int Quantity { get; set; }

        public string Publisher { get; set; }
        public DateTime? PublishedDate { get; set; }
        public decimal Price { get; set; }
        public string Language { get; set; }
        public string Description { get; set; }
        public int? PageCount { get; set; }
        public int TypeId { get; set; }

        // Ảnh bìa
        public string? ImageUrl { get; set; }
    }

    public class BookUpdateRequest
    {
        public int Id { get; set; }
        public string ISBN { get; set; }
        public int BookstoreId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Translator { get; set; }
        public int Quantity { get; set; }

        public decimal Price { get; set; }
        public string Publisher { get; set; }
        public DateTime? PublishedDate { get; set; }
        public string Language { get; set; }
        public string Description { get; set; }
        public int? PageCount { get; set; }
        public int TypeId { get; set; }

        // Ảnh bìa
        public IFormFile? ImageFile { get; set; }
    }
    public class BookUpdateDTO
    {
        public int Id { get; set; }
        public string ISBN { get; set; }
        public int BookstoreId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string? Translator { get; set; }
        public int Quantity { get; set; }

        public decimal Price { get; set; }
        public string Publisher { get; set; }
        public DateTime? PublishedDate { get; set; }
        public string Language { get; set; }
        public string Description { get; set; }
        public int? PageCount { get; set; }
        public int TypeId { get; set; }

        // Ảnh bìa
        public string? ImageUrl { get; set; }
    }

    public class BookBuyRequest
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public class BookRefundRequest
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
    }

    public class BookFilterRequest
    {
        public int? TypeId { get; set; }
        public decimal? Price { get; set; }
        public string? SearchValue { get; set; }
    }

}
