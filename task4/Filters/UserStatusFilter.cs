using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using task4.Data;

namespace task4.FIlters
{
    public class UserStatusFilter : IAsyncActionFilter
    {
        private readonly AppDbContext _db;

        public UserStatusFilter(AppDbContext db)
        {
            _db = db;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            string action = context.RouteData.Values["action"]?.ToString()?.ToLower() ?? "";

            if (!user.Identity.IsAuthenticated ||
                action == "login" ||
                action == "register" ||
                action == "logout" ||
                action == "confirmemail")
            {
                await next();
                return;
            }

            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out int userId))
            {
                var dbUser = await _db.Users.FindAsync(userId);

                if (dbUser == null || dbUser.Status.StartsWith("blocked"))
                {
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    if (context.Controller is Controller controller)
                    {
                        controller.TempData["ErrorMessage"] = "Your account has been blocked or deleted.";
                    }

                    context.Result = new RedirectToActionResult("Login", "Account", null);
                    return;
                }
            }

            await next();
        }
    }
}