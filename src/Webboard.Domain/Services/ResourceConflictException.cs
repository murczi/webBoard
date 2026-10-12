namespace Webboard.Domain.Services;
public sealed class ResourceConflictException() : InvalidOperationException("This item changed since the form was opened. Reload the page and review the latest data.");
