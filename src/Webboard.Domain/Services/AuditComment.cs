namespace Webboard.Domain.Services;

using Model.AuditLogs;

internal static class AuditComment {
    public static string Normalize(string comment) {
        var normalized = comment.Trim();
        if (normalized.Length is 0 or > AuditLogModel.MaxCommentLength)
            throw new ArgumentException(
                $"Audit comment must be between 1 and {AuditLogModel.MaxCommentLength} characters.",
                nameof(comment));
        return normalized;
    }
}
