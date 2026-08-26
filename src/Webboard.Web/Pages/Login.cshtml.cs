namespace Webboard.Web.Pages;

using System.ComponentModel.DataAnnotations;
using Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class LoginModel(JwtSessionService sessions) : PageModel {
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(GetSafeReturnUrl());

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) {
        if (!ModelState.IsValid)
            return Page();

        var token = await sessions.CreateTokenAsync(
            Input.UserName,
            Input.Password,
            Input.RememberMe,
            cancellationToken);
        if (token is null) {
            ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
            return Page();
        }

        Response.Cookies.Append(
            JwtOptions.CookieName,
            token,
            sessions.CreateCookieOptions(Input.RememberMe, Request.IsHttps));
        return LocalRedirect(GetSafeReturnUrl());
    }

    public IActionResult OnPostLogout() {
        Response.Cookies.Delete(JwtOptions.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return RedirectToPage("/Login");
    }

    private string GetSafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Page("/Index")!;

    public sealed class InputModel {
        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
