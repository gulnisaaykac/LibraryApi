//anguların göndereceği json şekli

namespace LibraryApi.Models;

public class LoginRequest
{
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
}