using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        RoleClaimType = "groupName"
    };
});
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

builder.Services.AddScoped<Library.DataAccess.DataBase>();
builder.Services.AddScoped<Library.DataAccess.BookRepository>();
builder.Services.AddScoped<Library.DataAccess.CategoryRepository>();
builder.Services.AddScoped<Library.DataAccess.UserRepository>();
builder.Services.AddScoped<Library.DataAccess.CityRepository>();
builder.Services.AddScoped<Library.DataAccess.QuizRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy =>
    {
        // Minikube UI origin degisken; nginx proxy ayni origin kullanir
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Angular");
app.UseAuthentication();
app.UseAuthorization();
// Production/Minikube HTTP; https redirect 404/loop yapmasin
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();


app.Run();
