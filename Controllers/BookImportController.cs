using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using UglyToad.PdfPig;

namespace LibraryApi.Controllers;

[ApiController] // web api controller
[Route("api/book-import")]//adres
[Authorize(Roles = "Admin")]//aksiyonlar admin token ister
public class BookImportController : ControllerBase//controllerbase http cevapları için temel sınıf
{
    private readonly IWebHostEnvironment _env;//proje klasörünün yolunu bilen ASP.NET servisi

    public BookImportController(IWebHostEnvironment env)//constructor
    {
        _env = env;
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
}