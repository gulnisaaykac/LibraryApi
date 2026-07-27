//http kapısını yazdık

/*using LibraryApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly LibraryDbContext _db;
    //sql den kontrolleri okumasi için

    public CategoriesController(LibraryDbContext db)
    {
        _db = db;
    }

    [HttpGet]//get isteği
    public async Task<ActionResult<List<Category>>> GetCategories()
        => await _db.Categories.ToListAsync();//tüm kategorileri listele
}//json u döndürür*/

//değiştirdik çünkü sp ile yapıcaz artık ef ile değil

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Library.DataAccess;
using Library.DataAccess.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryRepository _categories;

    public CategoriesController(CategoryRepository categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public ActionResult<List<Category>> GetCategories()
    {
        return _categories.GetCategories();
    }

    [Authorize(Roles ="Admin")]
    [HttpPost]
    public ActionResult<Category> CreateCategory(Category category)
    {
        var newId = _categories.InsertCategory(category);
        category.Id = newId;
        return CreatedAtAction(nameof(GetCategories), new { id = newId }, category);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public IActionResult DeleteCategory(int id)
    {
        _categories.DeleteCategory(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public IActionResult UpdateCategory(int id, Category category)
{
    category.Id = id;
    _categories.UpdateCategory(category);
    return NoContent();
}
}