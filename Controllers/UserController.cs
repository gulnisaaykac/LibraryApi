using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Library.DataAccess;
using Library.DataAccess.Models;

namespace LibraryApi.Controllers;

[ApiController]
[Route("api/[controller]")]

public class UsersController : ControllerBase
{
    private readonly UserRepository _users;

    public UsersController(UserRepository users)
    {
        _users = users;
    }//yukarıdaki 3 satır DI ile repository gelir program cs de zaten var yeniden ekleme 

    [Authorize(Roles ="Admin")]
    [HttpGet]
    public ActionResult<List<UserListItem>> GetUsers()
    {
        return _users.GetUsers();
    }

    [Authorize (Roles ="Admin")]
    [HttpPost("{id:int}/make-admin")]
    public IActionResult MakeAdmin (int id)
    {
        var result = _users.AddUserToAdmin(id);

        if (result == 0)
            return BadRequest(new { massage = " Admin Grubu Bulunamadi " });

        if (result == 2)
            return Ok(new { message = "kullanıcı zaten admin" });

        return Ok(new { message = "kullanıcı admin yapıldı" });
      
    }
}