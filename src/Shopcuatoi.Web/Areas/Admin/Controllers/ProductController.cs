﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using Shopcuatoi.Core.ApplicationServices;
using Shopcuatoi.Core.Domain.Models;
using Shopcuatoi.Infrastructure;
using Shopcuatoi.Infrastructure.Domain.IRepositories;
using Shopcuatoi.Web.Areas.Admin.ViewModels.Products;
using Shopcuatoi.Web.Areas.Admin.ViewModels.SmartTable;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Shopcuatoi.Web.Extensions;

namespace Shopcuatoi.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class ProductController : Controller
    {
        private readonly IRepository<Product> productRepository;
        private readonly IMediaService mediaService;
        private readonly IUrlSlugService urlSlugService;

        public ProductController(IRepository<Product> productRepository, IMediaService mediaService, IUrlSlugService urlSlugService)
        {
            this.productRepository = productRepository;
            this.mediaService = mediaService;
            this.urlSlugService = urlSlugService;
        }

        public IActionResult Get(long id)
        {
            var product = productRepository.Get(id);

            var productVm = new ProductViewModel
            {
                Name = product.Name,
                ShortDescription = product.ShortDescription,
                Description = product.Description,
                Specification = product.Specification,
                OldPrice = product.OldPrice,
                Price = product.Price,
                CategoryIds = product.Categories.Select(x => x.CategoryId).ToList(),
                ThumbnailImageUrl = mediaService.GetThumbnailUrl(product.ThumbnailImage)
            };

            foreach (var productMedia in product.Medias)
            {
                productVm.ProductMedias.Add(new ProductMediaVm
                {
                    Id = productMedia.Id,
                    MediaUrl = mediaService.GetThumbnailUrl(productMedia.Media)
                });
            }

            var attributes = from attr in product.AttributeValues
                             group attr by new
                             {
                                 attr.AttributeId,
                                 attr.Attribute.Name,
                                 attr.ProductId
                             }
            into g
                             select new ProductAttributeVm
                             {
                                 Id = g.Key.AttributeId,
                                 Name = g.Key.Name,
                                 Values = g.Select(x => x.Value).Distinct().ToList()
                             };

            productVm.Attributes = attributes.ToList();

            foreach (var variation in product.Variations)
            {
                productVm.Variations.Add(new ProductVariationVm
                {
                    Id = variation.Id,
                    Name = variation.Name,
                    PriceOffset = variation.PriceOffset,
                    AttributeCombinations = variation.AttributeCombinations.Select(x => new ProductAttributeCombinationVm
                    {
                        AttributeId = x.AttributeId,
                        AttributeName = x.Attribute.Name,
                        Value = x.Value
                    }).ToList()
                });
            }

            return Json(productVm);
        }

        public IActionResult List([FromBody] SmartTableParam param)
        {
            var products = productRepository.Query().Where(x => !x.IsDeleted);
            var gridData = products.ToSmartTableResult(
                param,
                x => new ProductListItem
                    {
                        Id = x.Id,
                        Name = x.Name,
                        CreatedOn = x.CreatedOn,
                        IsPublished = x.IsPublished
                    });

            return Json(gridData);
        }

        [HttpPost]
        public IActionResult Create(ProductForm model)
        {
            if (!ModelState.IsValid)
            {
                return Json(ModelState.ToDictionary());
            }

            var product = new Product
            {
                Name = model.Product.Name,
                SeoTitle = StringHelper.ToUrlFriendly(model.Product.Name),
                ShortDescription = model.Product.ShortDescription,
                Description = model.Product.Description,
                Specification = model.Product.Specification,
                Price = model.Product.Price,
                OldPrice = model.Product.OldPrice,
                IsPublished = model.Product.IsPublished
            };

            foreach (var attribute in model.Product.Attributes)
            {
                foreach (var value in attribute.Values)
                {
                    product.AddAttributeValue(new ProductAttributeValue
                    {
                        Value = value,
                        AttributeId = attribute.Id
                    });
                }
            }

            MapProductVariationVmToProduct(model, product);

            foreach (var categoryId in model.Product.CategoryIds)
            {
                var productCategory = new ProductCategory
                {
                    CategoryId = categoryId
                };
                product.AddCategory(productCategory);
            }

            SaveProductImages(model, product);

            productRepository.Add(product);
            productRepository.SaveChange();

            urlSlugService.Add(product.SeoTitle, product.Id, "Product");
            productRepository.SaveChange();

            return Ok();
        }

        private static void MapProductVariationVmToProduct(ProductForm model, Product product)
        {
            foreach (var variationVm in model.Product.Variations)
            {
                var variation = new ProductVariation
                {
                    Name = variationVm.Name,
                    PriceOffset = variationVm.PriceOffset
                };
                foreach (var combinationVm in variationVm.AttributeCombinations)
                {
                    variation.AddAttributeCombination(new ProductAttributeCombination
                    {
                        AttributeId = combinationVm.AttributeId,
                        Value = combinationVm.Value
                    });
                }
                product.AddProductVariation(variation);
            }
        }

        private void SaveProductImages(ProductForm model, Product product)
        {
            if (model.ThumbnailImage != null)
            {
                var fileName = SaveFile(model.ThumbnailImage);
                product.ThumbnailImage = new Media { FileName = fileName };
            }

            // Currently model binder cannot map the collection of file productImages[0], productImages[1]
            foreach (var file in Request.Form.Files)
            {
                if (file.ContentDisposition.Contains("productImages"))
                {
                    model.ProductImages.Add(file);
                }
            }

            foreach (var file in model.ProductImages)
            {
                var fileName = SaveFile(file);
                var productMedia = new ProductMedia
                {
                    Product = product,
                    Media = new Media { FileName = fileName }
                };
                product.AddMedia(productMedia);
            }
        }

        private string SaveFile(IFormFile file)
        {
            var originalFileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim('"');
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(originalFileName)}";
            mediaService.SaveMedia(file.OpenReadStream(), fileName);
            return fileName;
        }
    }
}