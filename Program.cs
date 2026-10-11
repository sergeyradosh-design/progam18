using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseInMemoryDatabase("SocialAppDb"));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication()
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = "fake-google-client-id.apps.googleusercontent.com";
        googleOptions.ClientSecret = "fake-google-client-secret";
    });

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    foreach (var role in new[] { "Admin", "Moderator", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    if (await userManager.FindByEmailAsync("admin@app.com") == null)
    {
        var adminUser = new IdentityUser { UserName = "admin@app.com", Email = "admin@app.com", EmailConfirmed = true };
        await userManager.CreateAsync(adminUser, "Admin123!");
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public enum PostVisibility { Public, Private, FriendsOnly }

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public PostVisibility Visibility { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool IsHiddenByModerator { get; set; }
}

public class Friendship
{
    public int Id { get; set; }
    public string User1Id { get; set; } = string.Empty;
    public string User2Id { get; set; } = string.Empty;
    public bool IsAccepted { get; set; }
}

public class AppIdentityDbContext : IdentityDbContext<IdentityUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options) { }
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
}

public class HomeController : Controller
{
    private readonly AppIdentityDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public HomeController(AppIdentityDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = _userManager.GetUserId(User);
        var allPosts = await _db.Posts.ToListAsync();

        var visiblePosts = new List<Post>();

        foreach (var post in allPosts)
        {
            if (post.IsHiddenByModerator && !User.IsInRole("Admin") && !User.IsInRole("Moderator"))
                continue;

            if (post.Visibility == PostVisibility.Public)
            {
                visiblePosts.Add(post);
            }
            else if (currentUserId != null && post.UserId == currentUserId)
            {
                visiblePosts.Add(post);
            }
            else if (post.Visibility == PostVisibility.FriendsOnly && currentUserId != null)
            {
                var isFriend = await _db.Friendships.AnyAsync(f =>
                    f.IsAccepted &&
                    ((f.User1Id == currentUserId && f.User2Id == post.UserId) ||
                     (f.User1Id == post.UserId && f.User2Id == currentUserId)));

                if (isFriend) visiblePosts.Add(post);
            }
        }

        var postsHtml = new StringBuilder();
        foreach (var p in visiblePosts)
        {
            var author = await _userManager.FindByIdAsync(p.UserId);
            var authorEmail = author?.Email ?? "Невідомий";

            postsHtml.Append($@"
                <div class='card mb-3 shadow-sm'>
                    <div class='card-body'>
                        <h5 class='card-title'>{p.Title} <small class='text-muted fs-6'>({p.Visibility})</small></h5>
                        <p class='card-text'>{p.Content}</p>
                        <small class='text-muted'>Автор: {authorEmail}</small>
                        {(User.IsInRole("Admin") || User.IsInRole("Moderator") || p.UserId == currentUserId ?
                            $"<div class='mt-2'><a href='/Home/DeletePost/{p.Id}' class='btn btn-sm btn-outline-danger'>Видалити/Приховати</a></div>" : "")}
                    </div>
                </div>");
        }

        var navLinks = User.Identity?.IsAuthenticated == true ?
            @"<a href='/Home/CreatePost' class='btn btn-primary me-2'>Створити пост</a>
              <a href='/Home/Friends' class='btn btn-secondary me-2'>Друзі</a>
              " + (User.IsInRole("Admin") ? "<a href='/Admin/Index' class='btn btn-warning me-2'>Адмін-панель</a>" : "") + @"
              <a href='/Account/Logout' class='btn btn-outline-danger'>Вийти</a>" :
            @"<a href='/Account/Login' class='btn btn-outline-primary me-2'>Увійти</a>
              <a href='/Account/Register' class='btn btn-success'>Реєстрація</a>";

        var html = $@"
            <!DOCTYPE html>
            <html lang='uk'>
            <head><meta charset='utf-8'><title>Головна</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
            <body class='bg-light py-4'>
                <div class='container' style='max-width: 800px;'>
                    <div class='d-flex justify-content-between align-items-center mb-4'>
                        <h2>Головна стрічка</h2>
                        <div>{navLinks}</div>
                    </div>
                    <div class='mt-4'>
                        {(visiblePosts.Count > 0 ? postsHtml.ToString() : "<p class='text-muted'>Немає доступних постів.</p>")}
                    </div>
                </div>
            </body>
            </html>";

        return Content(html, "text/html", Encoding.UTF8);
    }

    [Authorize]
    [HttpGet]
    public IActionResult CreatePost() => Content(@"
        <!DOCTYPE html><html lang='uk'><head><meta charset='utf-8'><title>Створити пост</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
        <body class='bg-light py-5'><div class='container' style='max-width: 500px;'><div class='card shadow p-4'>
            <h3>Новий пост</h3>
            <form action='/Home/CreatePost' method='post'>
                <div class='mb-3'><label class='form-label'>Заголовок</label><input type='text' name='title' class='form-control' required /></div>
                <div class='mb-3'><label class='form-label'>Зміст</label><textarea name='content' class='form-control' required></textarea></div>
                <div class='mb-3'><label class='form-label'>Видимість</label>
                    <select name='visibility' class='form-select'>
                        <option value='0'>Публічний (усі)</option>
                        <option value='1'>Приватний (тільки я)</option>
                        <option value='2'>Для друзів</option>
                    </select>
                </div>
                <button type='submit' class='btn btn-success w-100'>Опублікувати</button>
            </form>
        </div></div></body></html>", "text/html", Encoding.UTF8);

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreatePost(string title, string content, PostVisibility visibility)
    {
        var userId = _userManager.GetUserId(User);
        _db.Posts.Add(new Post { Title = title, Content = content, Visibility = visibility, UserId = userId! });
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    [Authorize]
    public async Task<IActionResult> DeletePost(int id)
    {
        var post = await _db.Posts.FindAsync(id);
        if (post != null)
        {
            var userId = _userManager.GetUserId(User);
            if (post.UserId == userId || User.IsInRole("Admin") || User.IsInRole("Moderator"))
            {
                _db.Posts.Remove(post);
                await _db.SaveChangesAsync();
            }
        }
        return RedirectToAction("Index");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Friends()
    {
        var userId = _userManager.GetUserId(User);
        var users = await _userManager.Users.Where(u => u.Id != userId).ToListAsync();
        var friendships = await _db.Friendships.Where(f => f.User1Id == userId || f.User2Id == userId).ToListAsync();

        var usersHtml = new StringBuilder();
        foreach (var u in users)
        {
            var rel = friendships.FirstOrDefault(f => f.User1Id == u.Id || f.User2Id == u.Id);
            string status = "Додати в друзі";
            string actionBtn = $"<a href='/Home/SendRequest/{u.Id}' class='btn btn-sm btn-primary'>Додати</a>";

            if (rel != null)
            {
                if (rel.IsAccepted)
                {
                    status = "Друзі";
                    actionBtn = "<span class='text-success fw-bold'>В друзях</span>";
                }
                else if (rel.User1Id == userId)
                {
                    status = "Запит надіслано";
                    actionBtn = "<span class='text-muted'>Очікування</span>";
                }
                else
                {
                    status = "Вхідний запит";
                    actionBtn = $"<a href='/Home/AcceptRequest/{rel.Id}' class='btn btn-sm btn-success'>Підтвердити</a>";
                }
            }

            usersHtml.Append($"<li class='list-group-item d-flex justify-content-between align-items-center'>{u.Email} {actionBtn}</li>");
        }

        var html = $@"
            <!DOCTYPE html><html lang='uk'><head><meta charset='utf-8'><title>Друзі</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
            <body class='bg-light py-4'><div class='container' style='max-width: 600px;'>
                <a href='/' class='btn btn-outline-secondary mb-3'>&larr; Назад</a>
                <h3>Користувачі та друзі</h3>
                <ul class='list-group mt-3'>{usersHtml}</ul>
            </div></body></html>";

        return Content(html, "text/html", Encoding.UTF8);
    }

    [Authorize]
    public async Task<IActionResult> SendRequest(string id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId != null && !await _db.Friendships.AnyAsync(f => (f.User1Id == userId && f.User2Id == id) || (f.User1Id == id && f.User2Id == userId)))
        {
            _db.Friendships.Add(new Friendship { User1Id = userId, User2Id = id, IsAccepted = false });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction("Friends");
    }

    [Authorize]
    public async Task<IActionResult> AcceptRequest(int id)
    {
        var friendship = await _db.Friendships.FindAsync(id);
        if (friendship != null)
        {
            friendship.IsAccepted = true;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction("Friends");
    }
}

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public AccountController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpGet]
    public IActionResult Register() => Content(@"
        <!DOCTYPE html><html lang='uk'><head><meta charset='utf-8'><title>Реєстрація</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
        <body class='bg-light py-5'><div class='container' style='max-width: 400px;'><div class='card shadow p-4'>
            <h3 class='text-center mb-3'>Реєстрація</h3>
            <form action='/Account/Register' method='post'>
                <div class='mb-3'><label class='form-label'>Email</label><input type='email' name='email' class='form-control' required /></div>
                <div class='mb-3'><label class='form-label'>Пароль</label><input type='password' name='password' class='form-control' required /></div>
                <button type='submit' class='btn btn-success w-100'>Зареєструватися</button>
            </form>
            <div class='text-center mt-3'><a href='/Account/Login'>Увійти</a></div>
        </div></div></body></html>", "text/html", Encoding.UTF8);

    [HttpPost]
    public async Task<IActionResult> Register(string email, string password)
    {
        var user = new IdentityUser { UserName = email, Email = email };
        var result = await _userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "User");
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            Console.WriteLine($"[ФЕЙКОВЕ ПОСИЛАННЯ ДЛЯ ПІДТВЕРДЖЕННЯ E-MAIL]: /Account/ConfirmEmail?userId={user.Id}&token={token}");

            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Home");
        }
        return RedirectToAction("Register");
    }

    [HttpGet]
    public IActionResult Login() => Content(@"
        <!DOCTYPE html><html lang='uk'><head><meta charset='utf-8'><title>Вхід</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
        <body class='bg-light py-5'><div class='container' style='max-width: 400px;'><div class='card shadow p-4'>
            <h3 class='text-center mb-3'>Вхід</h3>
            <form action='/Account/Login' method='post'>
                <div class='mb-3'><label class='form-label'>Email</label><input type='email' name='email' class='form-control' required /></div>
                <div class='mb-3'><label class='form-label'>Пароль</label><input type='password' name='password' class='form-control' required /></div>
                <button type='submit' class='btn btn-primary w-100 mb-2'>Увійти</button>
            </form>
            <form action='/Account/ExternalLogin' method='post'>
                <button type='submit' name='provider' value='Google' class='btn btn-danger w-100'>Увійти через Google</button>
            </form>
            <div class='text-center mt-3'><a href='/Account/Register'>Реєстрація</a></div>
        </div></div></body></html>", "text/html", Encoding.UTF8);

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, false, false);
        if (result.Succeeded) return RedirectToAction("Index", "Home");
        return RedirectToAction("Login");
    }

    [HttpPost]
    public IActionResult ExternalLogin(string provider)
    {
        var redirectUrl = Url.Action("ExternalLoginCallback", "Account");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> ExternalLoginCallback()
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null) return RedirectToAction("Login");

        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false);
        if (result.Succeeded) return RedirectToAction("Index", "Home");

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        await _userManager.CreateAsync(user);
        await _userManager.AddLoginAsync(user, info);
        await _userManager.AddToRoleAsync(user, "User");
        await _signInManager.SignInAsync(user, false);

        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }
}

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;

    public AdminController(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.ToListAsync();
        var rows = new StringBuilder();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            rows.Append($@"
                <tr>
                    <td>{u.Email}</td>
                    <td>{string.Join(", ", roles)}</td>
                    <td>
                        <a href='/Admin/ToggleRole?email={u.Email}&role=Admin' class='btn btn-sm btn-outline-warning'>Адмін</a>
                        <a href='/Admin/ToggleRole?email={u.Email}&role=Moderator' class='btn btn-sm btn-outline-info'>Модератор</a>
                        <a href='/Admin/ToggleRole?email={u.Email}&role=User' class='btn btn-sm btn-outline-secondary'>Користувач</a>
                    </td>
                </tr>");
        }

        var html = $@"
            <!DOCTYPE html><html lang='uk'><head><meta charset='utf-8'><title>Адмін-панель</title><link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head>
            <body class='bg-light py-4'><div class='container' style='max-width: 800px;'>
                <a href='/' class='btn btn-outline-secondary mb-3'>&larr; На головну</a>
                <h3>Панель адміністратора</h3>
                <table class='table table-bordered bg-white shadow-sm mt-3'>
                    <thead class='table-dark'><tr><th>Email</th><th>Ролі</th><th>Дії (Зміна ролі)</th></tr></thead>
                    <tbody>{rows}</tbody>
                </table>
            </div></body></html>", "text/html", Encoding.UTF8);

        return Content(html, "text/html", Encoding.UTF8);
    }

    public async Task<IActionResult> ToggleRole(string email, string role)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null)
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role);
        }
        return RedirectToAction("Index");
    }
}