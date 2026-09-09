using System.Text;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Minimal hand-rolled CSV writer — this repo has no CsvHelper (or equivalent) NuGet
/// package and CLAUDE.md's "no new NuGet packages" rule applies to overnight prompts.
/// RFC 4180-shaped: fields containing a comma, quote, or newline are wrapped in quotes,
/// with internal quotes doubled. Shared by every CSV export query so escaping logic
/// exists in exactly one place.
/// </summary>
public static class CsvUtils
{
    public const string Bom = "﻿";

    public static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        bool needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public static void AppendRow(StringBuilder sb, params string?[] fields)
    {
        sb.AppendLine(string.Join(',', fields.Select(EscapeField)));
    }
}
