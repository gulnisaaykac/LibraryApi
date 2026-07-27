namespace LibraryApi.Models;

public class RegisterRequest{

    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Password { get; set; } = "";

}
//kullanıcıyı users a ekler 
//kendi başına kimseyi çağırmaz 
//angularda json göstericek //olmazsa controllerda 3 ayri string ile uğraşılır json alanları karışır hata artar