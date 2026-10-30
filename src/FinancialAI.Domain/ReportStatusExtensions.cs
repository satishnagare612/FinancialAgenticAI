namespace FinancialAI.Domain;

/// <summary>
/// Wire-format mapping for <see cref="ReportStatus"/>.
///
/// Do NOT use <c>ReportStatus.ToString()</c> when writing status to
/// persistence, HTTP responses, or Service Bus messages — it produces
/// PascalCase ("NeedsReview") which does not match the lowercase
/// snake_case values the frontend's <c>ReportStatus</c> TypeScript union
/// expects ("needs_review"). Always use <see cref="ToWireString"/>.
/// </summary>
public static class ReportStatusExtensions
{
    /// <summary>
    /// The single source of truth for the on-the-wire status string.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if a new enum member is added without updating the mapping.
    /// This is deliberate: silently emitting an unexpected string would
    /// produce a status the frontend cannot render, and the throw surfaces
    /// the gap at the first write rather than at the next frontend bug
    /// report.
    /// </exception>
    public static string ToWireString(this ReportStatus status) => status switch
    {
        ReportStatus.Pending => "pending",
        ReportStatus.Running => "running",
        ReportStatus.NeedsReview => "needs_review",
        ReportStatus.Completed => "completed",
        ReportStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(
                nameof(status), status,
                "Unknown ReportStatus. Update ToWireString when adding enum members."),
    };
}