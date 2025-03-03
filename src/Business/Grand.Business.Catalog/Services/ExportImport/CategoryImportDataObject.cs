﻿using Grand.Business.Catalog.Services.ExportImport.Dto;
using Grand.Business.Core.Extensions;
using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Business.Core.Interfaces.Common.Seo;
using Grand.Business.Core.Interfaces.ExportImport;
using Grand.Business.Core.Interfaces.Storage;
using Grand.Domain.Catalog;
using Grand.Domain.Media;
using Grand.Domain.Seo;
using Grand.Infrastructure.Mapper;
using Microsoft.AspNetCore.StaticFiles;

namespace Grand.Business.Catalog.Services.ExportImport
{
    public class CategoryImportDataObject : IImportDataObject<CategoryDto>
    {
        private readonly ICategoryService _categoryService;
        private readonly IPictureService _pictureService;
        private readonly ICategoryLayoutService _categoryLayoutService;
        private readonly ISlugService _slugService;
        private readonly ILanguageService _languageService;

        private readonly SeoSettings _seoSetting;

        public CategoryImportDataObject(
            ICategoryService categoryService,
            IPictureService pictureService,
            ICategoryLayoutService categoryLayoutService,
            ISlugService slugService,
            ILanguageService languageService,
            SeoSettings seoSetting)
        {
            _categoryService = categoryService;
            _pictureService = pictureService;
            _categoryLayoutService = categoryLayoutService;
            _slugService = slugService;
            _languageService = languageService;

            _seoSetting = seoSetting;
        }

        public async Task Execute(IEnumerable<CategoryDto> data)
        {
            foreach (var item in data)
            {
                await Import(item);
            }
        }

        protected async Task Import(CategoryDto categoryDto)
        {
            var category = await _categoryService.GetCategoryById(categoryDto.Id);
            var isNew = category == null;
            if (category == null) category = categoryDto.MapTo<CategoryDto, Category>();
            else categoryDto.MapTo(category);

            if (isNew) await _categoryService.InsertCategory(category);
            else await _categoryService.UpdateCategory(category);

            if (!ValidCategory(category)) return;

            await UpdateCategoryData(categoryDto, category);
        }

        protected async Task<Category> UpdateCategoryData(CategoryDto categoryDto, Category category)
        {
            if (string.IsNullOrEmpty(category.CategoryLayoutId))
                category.CategoryLayoutId = (await _categoryLayoutService.GetAllCategoryLayouts()).FirstOrDefault().Id;
            else
            {
                var layout = await _categoryLayoutService.GetCategoryLayoutById(category.CategoryLayoutId);
                if (layout == null)
                    category.CategoryLayoutId = (await _categoryLayoutService.GetAllCategoryLayouts()).FirstOrDefault().Id;
            }
            if(!string.IsNullOrEmpty(category.ParentCategoryId))
            {
                var parentCategory = await _categoryService.GetCategoryById(category.ParentCategoryId);
                if (parentCategory == null)
                    category.ParentCategoryId = string.Empty;
            }
            if (!string.IsNullOrEmpty(categoryDto.Picture))
            {
                var _picture = await LoadPicture(categoryDto.Picture, category.Name, category.PictureId);
                if (_picture != null)
                    category.PictureId = _picture.Id;
            }

            var sename = category.SeName ?? category.Name;
            sename = await category.ValidateSeName(sename, category.Name, true, _seoSetting, _slugService, _languageService);
            category.SeName = sename;
            await _categoryService.UpdateCategory(category);
            await _slugService.SaveSlug(category, sename, "");
            return category;
        }

        protected virtual bool ValidCategory(Category category)
        {
            if (string.IsNullOrEmpty(category.Name))
                return false;

            return true;
        }

        /// <summary>
        /// Creates or loads the image
        /// </summary>
        /// <param name="picturePath">The path to the image file</param>
        /// <param name="name">The name of the object</param>
        /// <param name="picId">Image identifier, may be null</param>
        /// <returns>The image or null if the image has not changed</returns>
        protected virtual async Task<Picture> LoadPicture(string picturePath, string name, string picId = "")
        {
            if (string.IsNullOrEmpty(picturePath) || !File.Exists(picturePath))
                return null;

            var mimeType = GetMimeTypeFromFilePath(picturePath);
            var newPictureBinary = File.ReadAllBytes(picturePath);
            var pictureAlreadyExists = false;
            if (!string.IsNullOrEmpty(picId))
            {
                //compare with existing product pictures
                var existingPicture = await _pictureService.GetPictureById(picId);

                var existingBinary = await _pictureService.LoadPictureBinary(existingPicture);
                //picture binary after validation (like in database)
                var validatedPictureBinary = _pictureService.ValidatePicture(newPictureBinary, mimeType);
                if (existingBinary.SequenceEqual(validatedPictureBinary) ||
                    existingBinary.SequenceEqual(newPictureBinary))
                {
                    pictureAlreadyExists = true;
                }
            }

            if (pictureAlreadyExists) return null;

            var newPicture = await _pictureService.InsertPicture(newPictureBinary, mimeType,
                _pictureService.GetPictureSeName(name));
            return newPicture;
        }
        protected virtual string GetMimeTypeFromFilePath(string filePath)
        {
            new FileExtensionContentTypeProvider().TryGetContentType(filePath, out string mimeType);
            //set to jpeg in case mime type cannot be found
            if (mimeType == null)
                mimeType = "image/jpeg";
            return mimeType;
        }
    }
}
