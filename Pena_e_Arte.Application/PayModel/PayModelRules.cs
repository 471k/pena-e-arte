using FluentValidation;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.PayModel;

/// <summary>Input rules shared by the studio and artist pay-model writes (A07a).</summary>
internal static class PayModelRules
{
    /// <summary>The largest flat fee accepted; well above any real session price, it only stops typos.</summary>
    public const decimal MaxFlatFee = 1_000_000m;

    /// <summary>Only the four arrangement names, exactly; never a number ("1") or another casing.</summary>
    public static bool TryParseArrangement(string? value, out ArtistPayArrangement arrangement)
    {
        arrangement = default;
        return value is not null
               && Enum.GetNames<ArtistPayArrangement>().Contains(value, StringComparer.Ordinal)
               && Enum.TryParse(value, ignoreCase: false, out arrangement);
    }

    /// <summary>0–100 with at most 2 decimals, the precision of the decimal(5,2) column.</summary>
    public static IRuleBuilderOptions<T, decimal?> ValidCommissionRate<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(r => r is null || (r.Value is >= 0m and <= 100m && decimal.Round(r.Value, 2) == r.Value))
            .WithMessage("Commission rate must be a percentage from 0 to 100 with at most 2 decimals.");

    public static IRuleBuilderOptions<T, decimal?> ValidFlatFee<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.Must(f => f is null or >= 0m and <= MaxFlatFee)
            .WithMessage($"Flat fee must be between 0 and {MaxFlatFee:N0}.");
}
