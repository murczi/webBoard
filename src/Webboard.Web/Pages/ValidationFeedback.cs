namespace Webboard.Web.Pages;
using Microsoft.AspNetCore.Mvc.ModelBinding;

public static class ValidationFeedback {
    public static string FirstError(ModelStateDictionary state, string fallback) => state.Values
        .SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage)
        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? fallback;
}
