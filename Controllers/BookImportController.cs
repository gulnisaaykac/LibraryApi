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
    private readonly CategoryRepository _categories;//DI
    private readonly QuizRepository _quiz;//quiz sorularini kaydetmek icin

    public BookImportController(//constructor
        IWebHostEnvironment env,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        BookRepository books,
        CityRepository cities,
        CategoryRepository categories,
        QuizRepository quiz)
    {
        _env = env;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _books = books;
        _cities = cities;//bu parametreler atilmazsa field hep null kalır finalize da patlar
        _categories = categories;
        _quiz = quiz;
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
            var maxPages = pageCount; //taranacak sayfa sayisi burda belirtiliyor

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
            pagesUsed = pageCount,
            text = markdown,
            progress = 50,
            message = "PDF metne cevrildi (tum sayfalar)."
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
            var maxPages = document.NumberOfPages;//en fazla 10 sayfa
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
            "JSON keys: title, author, category, city, summary. " +
            "summary: 2-4 paragraph overview of the book in Turkish. " +
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
            summary = Get("summary"),
            progress = 75,
            message = "AI bilgileri cikardi."
        });
    }

    [HttpPost("{fileId}/cover")]
    public async Task<IActionResult> Cover(string fileId, [FromBody] CoverImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return BadRequest("fileId gerekli.");

        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit))
            return BadRequest("Gecersiz fileId.");

        if (request == null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("title gerekli.");

        var title = request.Title.Trim();
        var author = (request.Author ?? "").Trim();
        var query = string.IsNullOrWhiteSpace(author) ? title : $"{title} {author}";

        var client = _httpClientFactory.CreateClient();
        var url =
            "https://openlibrary.org/search.json?limit=1&q=" +
            Uri.EscapeDataString(query);

        using var resp = await client.GetAsync(url);
        var respText = await resp.Content.ReadAsStringAsync();

        string coverUrl = "";

        if (resp.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(respText);
            if (doc.RootElement.TryGetProperty("docs", out var docs) &&
                docs.GetArrayLength() > 0)
            {
                var first = docs[0];
                if (first.TryGetProperty("cover_i", out var coverId) &&
                    coverId.ValueKind == JsonValueKind.Number)
                {
                    coverUrl = $"https://covers.openlibrary.org/b/id/{coverId.GetInt32()}-L.jpg";
                }
            }
        }

        if (string.IsNullOrWhiteSpace(coverUrl))
            coverUrl = "https://covers.openlibrary.org/b/id/0-L.jpg";

        return Ok(new
        {
            fileId,
            coverUrl,
            progress = 80,
            message = "Kapak URL bulundu."
        });
    }

    [HttpPost("{fileId}/quiz")]
    public async Task<IActionResult> Quiz(string fileId, [FromBody] QuizImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return BadRequest("fileId gerekli.");

        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit))
            return BadRequest("Gecersiz fileId.");

        if (request == null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("title gerekli.");

        var apiKey = _config["Groq:ApiKey"];
        var model = _config["Groq:Model"] ?? "llama-3.3-70b-versatile";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("BURAYA"))
            return StatusCode(500, "Groq ApiKey ayarli degil.");

        var systemPrompt =
            "You create a quiz about a book. " +
            "Reply with ONLY valid JSON, no markdown. " +
            "JSON shape: { \"questions\": [ ... ] }. " +
            "Create exactly 20 questions. Each question object keys: " +
            "questionText, optionA, optionB, optionC, optionD, correctOption, explanation, sortOrder. " +
            "correctOption must be A, B, C, or D. " +
            "explanation is a short Turkish explanation of the correct answer. " +
            "sortOrder is 1 to 20. Questions and options in Turkish.";

        var userContent =
            $"Title: {request.Title}\n" +
            $"Author: {request.Author}\n" +
            $"Summary:\n{request.Summary}";

        var requestBody = new
        {
            model,
            temperature = 0.3,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userContent }
            }
        };

        var client = _httpClientFactory.CreateClient();

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.groq.com/openai/v1/chat/completions");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(
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

        using var quizJson = JsonDocument.Parse(content);

        if (!quizJson.RootElement.TryGetProperty("questions", out var questionsEl) ||
            questionsEl.ValueKind != JsonValueKind.Array)
            return BadRequest("AI quiz formati hatali.");

        var questions = new List<object>();
        var order = 1;

        foreach (var q in questionsEl.EnumerateArray())
        {
            string Get(string name) =>
                q.TryGetProperty(name, out var p) ? (p.GetString() ?? "") : "";

            var sortOrder = order;
            if (q.TryGetProperty("sortOrder", out var so) && so.TryGetInt32(out var n) && n > 0)
                sortOrder = n;

            questions.Add(new
            {
                questionText = Get("questionText"),
                optionA = Get("optionA"),
                optionB = Get("optionB"),
                optionC = Get("optionC"),
                optionD = Get("optionD"),
                correctOption = Get("correctOption"),
                explanation = Get("explanation"),
                sortOrder
            });

            order++;
            if (order > 20) break;
        }

        return Ok(new
        {
            fileId,
            questions,
            progress = 90,
            message = "Quiz sorulari uretildi."
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
        var category = _categories.EnsureCategory(request.Category ?? "");

        var book = new Book
        {
            Title = request.Title.Trim(),
            Author = request.Author.Trim(),
            Category = category.Name,
            City = city.Name,    //id vermşyoruz sql insert sonrası olacak
            Summary = request.Summary ?? "",//AI ozeti
            CoverUrl = request.CoverUrl ?? ""//Open Library kapak URL
        };

        var bookId = _books.InsertBook(book);

        // quiz endpointinden gelen sorulari kitaba bagla (en fazla 20)
        if (request.Questions != null)
        {
            var sort = 1;
            foreach (var q in request.Questions.Take(20))
            {
                _quiz.InsertQuestion(new QuizQuestion
                {
                    BookId = bookId,
                    QuestionText = q.QuestionText ?? "",
                    OptionA = q.OptionA ?? "",
                    OptionB = q.OptionB ?? "",
                    OptionC = q.OptionC ?? "",
                    OptionD = q.OptionD ?? "",
                    CorrectOption = q.CorrectOption ?? "",
                    Explanation = q.Explanation ?? "",
                    SortOrder = q.SortOrder > 0 ? q.SortOrder : sort
                });
                sort++;
            }
        }

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
    public string Summary { get; set; } = "";//AI ozeti
    public string CoverUrl { get; set; } = "";//kapak URL
    public List<QuizQuestionDto> Questions { get; set; } = new();//20 quiz sorusu
    
}

// finalize body icindeki tek soru modeli
public class QuizQuestionDto
{
    public string QuestionText { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    public string CorrectOption { get; set; } = "";// A/B/C/D
    public string Explanation { get; set; } = "";
    public int SortOrder { get; set; }
}

public class QuizImportRequest
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Summary { get; set; } = "";
}


public class CoverImportRequest
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
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