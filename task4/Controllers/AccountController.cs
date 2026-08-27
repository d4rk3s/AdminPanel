using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using task4.Data;
using task4.Models;
using task4.Services;

namespace task4.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EmailService _emailService;

        public AccountController(AppDbContext db, EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Index(string sortOrder)
        {
            ViewData["CurrentSort"] = sortOrder;

            ViewBag.NameSortParm = sortOrder == "name" ? "name_desc" : "name";
            ViewBag.EmailSortParm = sortOrder == "email" ? "email_desc" : "email";
            ViewBag.DateSortParm = sortOrder == "date" ? "date_desc" : "date";
            ViewBag.StatusSortParm = sortOrder == "status" ? "status_desc" : "status";

            var users = _db.Users.AsQueryable();

            users = sortOrder switch
            {
                "name" => users.OrderBy(u => u.Name),
                "name_desc" => users.OrderByDescending(u => u.Name),
                "email" => users.OrderBy(u => u.Email),
                "email_desc" => users.OrderByDescending(u => u.Email),
                "date" => users.OrderBy(u => u.LastLoginAt),
                "date_desc" => users.OrderByDescending(u => u.LastLoginAt),
                "status" => users.OrderBy(u => u.Status),
                "status_desc" => users.OrderByDescending(u => u.Status),
                _ => users.OrderByDescending(u => u.LastLoginAt ?? u.RegisteredAt)
            };

            return View(await users.ToListAsync());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Actions(string actionType, int[] selectedIds)
        {
            if (actionType == "deleteUnverified")
            {
                var unverifiedUsers = await _db.Users.Where(u => u.Status == "unverified" || u.Status == "blocked_unverified").ToListAsync();
                _db.Users.RemoveRange(unverifiedUsers);
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            if (selectedIds != null && selectedIds.Length > 0)
            {
                var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int.TryParse(currentUserIdStr, out int currentUserId);
                bool isSelfAffected = selectedIds.Contains(currentUserId);

                var users = await _db.Users.Where(u => selectedIds.Contains(u.Id)).ToListAsync();

                foreach (var u in users)
                {
                    switch (actionType)
                    {
                        case "block":
                            if (u.Status == "unverified")
                                u.Status = "blocked_unverified";
                            else if (u.Status == "active")
                                u.Status = "blocked";
                            _db.Entry(u).State = EntityState.Modified;
                            break;

                        case "unblock":
                            if (u.Status == "blocked_unverified")
                                u.Status = "unverified";
                            else if (u.Status == "blocked")
                                u.Status = "active";
                            _db.Entry(u).State = EntityState.Modified;
                            break;

                        case "delete":
                            _db.Users.Remove(u);
                            break;
                    }
                }

                await _db.SaveChangesAsync();

                if (isSelfAffected && (actionType == "block" || actionType == "delete"))
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    TempData["ErrorMessage"] = actionType == "block"
                        ? "You blocked your own account."
                        : "You deleted your own account.";
                    return RedirectToAction("Login", "Account");
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string name, string email, string password)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Please fill in all fields.");
                return View();
            }

            var user = new User
            {
                Name = name,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Status = "unverified"
            };

            try
            {
                _db.Users.Add(user);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "A user with this email already exists.");
                return View();
            }

            var confirmLink = Url.Action("ConfirmEmail", "Account", new { userId = user.Id }, Request.Scheme);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendEmailAsync(user.Email, confirmLink!);
                }
                catch (Exception)
                {
                }
            });

            TempData["SuccessMessage"] = "Registration successful! Instructions have been sent to your email.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid login or password.");
                return View();
            }

            if (user.Status != null && user.Status.StartsWith("blocked"))
            {
                ModelState.AddModelError("", "Your account is blocked.");
                return View();
            }

            var minskTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Minsk");
            user.LastLoginAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, minskTimeZone);
            await _db.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                if (user.Status == "unverified")
                {
                    user.Status = "active";
                    await _db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Email successfully confirmed!";
                }
            }
            return RedirectToAction(nameof(Login));
        }
    }
}