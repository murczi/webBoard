namespace Webboard.Web.Pages;

using System.ComponentModel.DataAnnotations;
using Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class RegisterModel(JwtSessionService sessions) : PageModel {
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

        var token = await sessions.RegisterAsync(
            Input.UserName,
            Input.Password,
            Input.RememberMe,
            cancellationToken);
        if (token is null) {
            ModelState.AddModelError(
                "Input.UserName",
                "That username is already registered.");
            return Page();
        }

        Response.Cookies.Append(
            JwtOptions.CookieName,
            token,
            sessions.CreateCookieOptions(Input.RememberMe, Request.IsHttps));
        return LocalRedirect(GetSafeReturnUrl());
    }

    private string GetSafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Page("/Index")!;

    public sealed class InputModel {
        [Required]
        [StringLength(64, MinimumLength = 3)]
        [RegularExpression(
            @"^[A-Za-z0-9][A-Za-z0-9._-]*$",
            ErrorMessage = "Use letters, numbers, dots, underscores, or hyphens.")]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
