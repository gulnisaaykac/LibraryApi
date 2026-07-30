using Library.DataAccess;
using Library.DataAccess.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;

namespace LibraryApi.Controllers;

[ApiController] // web api controller
[Route("api/book-import")]//adres
[Authorize(Roles = "Admin")]//aksiyonlar admin token ister
public class BookImportController : ControllerBase//controllerbase http cevapları için temel sınıf
{
    private readonly IWebHostEnvironment _env;//proje klasörünün yolunu bilen ASP.NET servisi
    private readonly IHttpClientFactory _httpClientFactory; //groq a istek 
    private readonly IConfiguration _config; //groq:Apikey - model okumak
    private readonly BookRepository _books;//DI
    private readonly CityRepository _cities;//DI

    public BookImportController(//constructor
        IWebHostEnvironment env,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        BookRepository books,
        CityRepository cities)
    {
        _env = env;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _books = books;
        _cities = cities;//bu parametreler atilmazsa field hep null kalır finalize da patlar
    }



    [HttpPost("upload")]//post
    public async Task<IActionResult> Upload(IFormFile file)//async task<> (dosya yazarken await kullanacağız)
                                                           //IformFile file ----> postman/angulardan gelen pdf
    {
        if (file == null || file.Length == 0)
            return BadRequest("Dosya yok.");

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Sadece PDF yuklenebilir.");

        var uploadDir = Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(uploadDir);

        var fileId = Guid.NewGuid().ToString("N");// guid  id----> çakışmasın diye
        var savedName = fileId + ".pdf"; // fileId ----->bu id ile ilgili dosyayı bulacağız
        var fullPath = Path.Combine(uploadDir, savedName); // fullPath----> diskteki tam yol 

        await using (var stream = System.IO.File.Create(fullPath))//file.Create----> boş dosya aç 
        {
            await file.CopyToAsync(stream);//CopyToAsync----> gelen PDF i oraya kopyala
            //await----> kopyalama bitene kadar bekle
        }// dosyayı diske yaz     
        //dosyayı geçici tutulan yer burasi 

        // Sonraki adimlar bu fileId ile dosyayi bulacak5
        return Ok(new
        {
            fileId,//sonrakş adımda markdown/ai burayı kullanacak
            fileName = file.FileName,
            progress = 25, //ilerleme kutusında yüzde 25
            message = "PDF yuklendi (gecici)."
        });
    }

    [HttpPost("{fileId}/extract")]
    public IActionResult Extract(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return BadRequest("fileId gerekli.");

        // Guvenlik: sadece hex id (path traversal engeli)
        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit))
            return BadRequest("Gecersiz fileId.");

        var pdfPath = Path.Combine(_env.ContentRootPath, "App_Data", "uploads", fileId + ".pdf");//dosya yolu
        if (!System.IO.File.Exists(pdfPath))//dosya yoksa 404
            return NotFound("PDF bulunamadi. Once upload yap.");


        var sb = new StringBuilder();
        int pageCount;

        using (var document = PdfDocument.Open(pdfPath))//pdfi aç  -- using--> iş bitince dosyyı bırak
        {
            pageCount = document.NumberOfPages;
            var maxPages = Math.Min(10, pageCount); //taranacak sayfa sayisi burda belirtiliyor

            for (var i = 1; i <= maxPages; i++)
            {
                var page = document.GetPage(i);
                var text = page.Text?.Trim() ?? "";

                sb.AppendLine($"## Sayfa {i}"); //basit markdown baslik
                sb.AppendLine();
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }

        var markdown = sb.ToString().Trim();

        return Ok(new
        {
            fileId,
            pageCount,
            pagesUsed = Math.Min(10, pageCount),
            text = markdown,
            progress = 50,
            message = "PDF metne cevrildi (ilk sayfalar)."
        });
    }

    [HttpPost("{fileId}/analyze")]//post isteği 
    public async Task<IActionResult> Analyze(string fileId)//async var groq a giderken await kullanacağız
    {
        if (string.IsNullOrEmpty(fileId))
            return BadRequest("fileId gerekli");

        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit))
            return BadRequest("geçersiz fileId");

        var pdfPath = Path.Combine(_env.ContentRootPath, "App_Data", "uploads", fileId+ ".pdf");

        if (!System.IO.File.Exists(pdfPath))
            return NotFound("pdf bulunamadı!! once upload yap.");


        var sb = new StringBuilder();//sayfa metinleri biriktirir

        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))//PdfPig ile PDF aç 
        {
            var maxPages = Math.Min(10, document.NumberOfPages);//en fazla 10 sayfa
            for (var i = 1; i<= maxPages; i++)
            {
                var page = document.GetPage(i);
                sb.AppendLine(page.Text?.Trim() ?? "");
                sb.AppendLine();

            }
        }

        var text = sb.ToString().Trim();

        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("PDF'den metin çıkarılamadı.");

        // Metin cok uzunsa kisalt (token limiti)
        if (text.Length > 12000)
            text = text.Substring(0, 12000);

        var apiKey = _config["Groq:ApiKey"];
        var model = _config["Groq:Model"] ?? "llama-3.3-70b-versatile";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("BURAYA"))
            return StatusCode(500, "Groq ApiKey ayarli degil.");

        // 2) Groq'a gidecek prompt
        var systemPrompt =
            "You extract book metadata from PDF text. " +
            "Reply with ONLY valid JSON, no markdown, no extra text. " +
            "JSON keys: title, author, category, city. " +
            "If unknown, use empty string. category should be a short genre. " +
            "city is publication/print city if mentioned, else empty string.";

        var requestBody = new
        {
            model,//hangi groq modeli
            temperature = 0.2, //daha tutarlı/az "uyfurma"
            response_format = new { type = "json_object" }, //cevap json olsun 
            messages = new object[]
            {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = text }
            }
        };

        var client = _httpClientFactory.CreateClient();//http istemci

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.groq.com/openai/v1/chat/completions");//openAI uyumlu chat endpoint

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);//kimlik doğrulama

        req.Content = new StringContent(//body i json stringi yap

            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var resp = await client.SendAsync(req);

        var respText = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, "Groq hatasi: " + respText);

        using var doc = JsonDocument.Parse(respText);

        var content = doc.RootElement

            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        using var meta = JsonDocument.Parse(content);

        string Get(string name) =>
            meta.RootElement.TryGetProperty(name, out var p) ? (p.GetString() ?? "") : "";
        return Ok(new
        {
            fileId,
            title = Get("title"),
            author = Get("author"),
            category = Get("category"),
            city = Get("city"),
            progress = 75,
            message = "AI bilgileri cikardi."
        });
    }

    [HttpPost("{fileId}/finalize")]
    public IActionResult Finalize(string fileId, [FromBody] FinalizeImportRequest request)
    {
        if (string.IsNullOrEmpty(fileId))
            return BadRequest("file ID gerekli");

        if (fileId.Length !=32 || !fileId.All(Uri.IsHexDigit))
            return BadRequest("Geçersiz File ID");

        if (request== null) //body hiç gelmemiş json bozuk
            return BadRequest("body gerekli");

        if (string.IsNullOrWhiteSpace(request.Title) ||string.IsNullOrWhiteSpace(request.Author))
            return BadRequest("title ve author zorunlu");

        var pdfPath = Path.Combine(_env.ContentRootPath, "App_Data", "uploads", fileId + ".pdf");
        if(!System.IO.File.Exists(pdfPath))
            return NotFound("Pdf bulunamadi önce upload yap");/*geçici dosyalar hala duruyor mu? 
                                                               yoksa zaten silinmiş yanlış id 404
                                                                finalize hem kaydedip hem de geçici 
                                                                pdf i sileceği için önce kontrol et */

        var city = _cities.EnsureCity(request.City ?? "");

        var book = new Book
        {
            Title = request.Title.Trim(),
            Author = request.Author.Trim(),
            Category = (request.Category ?? "").Trim(),
            City = city.Name    //id vermşyoruz sql insert sonrası olacak
        };

        var bookId = _books.InsertBook(book);

        System.IO.File.Delete(pdfPath);//geçici pdf i sil 

        return Ok(new
        {
            fileId,
            bookId,
            title = book.Title,
            author = book.Author,
            category = book.Category,
            city = book.City,
            progress = 100,
            message = "Kitap kaydedildi, gecici PDF silindi."
        });
    }

}/* akış özeti --Fİnalize öncesi 
  * 
  * fileId kontrol
    - PDF bul
    - ilk 10 sayfa metin
    - Groq’a gönder
    - JSON’dan title/author/category/city al
    - progress: 75
*/

//altta yaptığımız şey finalize a gelecek JSON un c# karşılığı

public class FinalizeImportRequest // Body için DTO/ istek modeli
{

    public string City { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
    public string Category { get; set; }
    
}


//neden ayri classlar kullandık kullanmasak bu kadar net okumazdı bodyi elle parçalamak gerirdi ZOR

//daha hızlı olması adına LibraryApi/Models içerisine değil buraya atadım çünkü sadece burada kullanılacak eğer ihtiyaç olursa oraya taşırım




/*fileId + body (title/author/category/city)
        │
        ▼
   kontroller (id, body, title/author)
        │
        ▼                                            
   PDF var mı?
        │
        ▼
   EnsureCity  →  gerekirse Cities'e ekle
        │
        ▼
   InsertBook  →  Books'a yaz, bookId al
        │
        ▼
   PDF sil
        │
        ▼
   200 + progress: 100*/