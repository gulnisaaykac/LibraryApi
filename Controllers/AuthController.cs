using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Library.DataAccess;
using Library.DataAccess.Models; // LoginResult, GroupInfo
using LibraryApi.Models;         // LoginRequest, RegisterRequest

namespace LibraryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase {//auth controller dışarı açılan kapi 
    
    private readonly UserRepository _users;
    private readonly IConfiguration _config;

    public AuthController(UserRepository users , IConfiguration config){

        _users = users;
        _config = config;
    }

    [HttpPost("login")]

     public IActionResult Login([FromBody] LoginRequest request)
    {
        //string pwd = request.Password.
        var user = _users.Login(request.Name, request.Password);

        if (user == null)
            return Unauthorized(new { message = "Kullanici adi veya sifre yanlis." });

        var claims = new List<Claim>{

            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("surname", user.Surname),
        };
        
        foreach (var g in user.Groups)
        {
            claims.Add(new Claim("groupName", g.GroupName));
            claims.Add(new Claim("groupId", g.Id.ToString()));
        }

        var key = new SymmetricSecurityKey(

            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(

            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new{

            token = tokenString,
            name = user.Name,
            surname = user.Surname,
            groups = user.Groups

        });
     }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)//register request--> body
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Surname) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Isim, soyisim ve sifre zorunlu." });
        }

        var newId = _users.Register(request.Name, request.Surname, request.Password);

        if (newId == 0)
            return Conflict(new { message = "Bu isim zaten kayitli." });

        return Created("", new { id = newId, name = request.Name });
    }//authorize koymadık çünkü kayıt alanı herkese açık
} //login ile aynı controllerı kullandık çünkü ikisi de kimlik işi ekstra bi tane daha açtırıp ayni işi yaptırmaya gerek yok
  //ama hiç controller olmasaydı da dışarıdan istek giremezdik 