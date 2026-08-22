using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Single source of truth for which ReportCategory values are High severity. Backs two
/// things: whether filing the report fires the immediate owner+issuer email
/// (ConductReportNotifier), and whether an owner (as opposed to only the issuer) is permitted
/// to change a report's status (ConductReportAuthorizationGuard.EnsureCanChangeStatus).
/// Deliberately a static classification, not a stored column on ConductReport — one place to
/// change if the taxonomy is revised later.
/// </summary>
public static class ReportCategoryClassifier
{
    private static readonly HashSet<ReportCategory> HighSeverity =
    [
        ReportCategory.Scam,
        ReportCategory.SexualMisconduct,
        ReportCategory.UnsafeHygienePractices,
        ReportCategory.Harassment,
        ReportCategory.Discrimination,
    ];

    public static bool IsHighSeverity(ReportCategory category) => HighSeverity.Contains(category);
}
