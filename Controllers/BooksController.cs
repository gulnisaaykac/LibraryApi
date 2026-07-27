using Library.DataAccess;
using Library.DataAccess.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]//web api controlleri olduğunu belirtir bu iste uygulamad-nın backend e açılan kapısıdır
[Route("api/[controller]")]//route attribute u ile controllerin hangi route a karşılık geldiğini belirleriz(url)
//controller sınıfının adi ile url yi birbirine bağlar

public class BooksController : ControllerBase
{
    private readonly BookRepository _books;

    public BooksController(BookRepository books)
    {
        _books = books;
        //dışardan gelen veritabanı bağlantısını bu controller a kaydeder
    }

    [HttpGet]//bu metodun http get isteğine cevap vereceğini belirtir
    public ActionResult<List<Book>> GetBooks()
    {/* => await _db.Books.ToListAsync();-----> burayı kaldırdık çünkü ef gitti*/
         return _books.GetBooks();
    }
    //await = asenkron iş bitsin diye bekle; uygulama donmasın diye modern C# yolu.
    // eğer async olmasaydı sucunu bu metodun işi bitene kadar kullanılan thread üzerinde bekler bu da sistemin donmasına sebebiyet verir


    [Authorize(Roles = "Admin")]
    [HttpPost]
    public ActionResult<Book> CreateBook(Book book)
    {
        var newId = _books.InsertBook(book);
        book.Id = newId;
        return CreatedAtAction(nameof(GetBooks), new { id = newId }, book);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public IActionResult DeleteBook(int id)
    {
        _books.DeleteBook(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
public IActionResult UpdateBook(int id, Book book)
{
    book.Id = id;
    _books.UpdateBook(book);
    return NoContent();
}

}
