using FluentValidation;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.Common;

public static class TattooStyleValidationExtensions
{
    /// <summary>Validates an optional list of artist specializations against the canonical
    /// <see cref="TattooStyle"/> values — shared by every place that accepts a
    /// Specializations list (artist create/update, own-profile, solo-studio-join invite).</summary>
    public static IRuleBuilderOptions<T, List<string>?> MustBeValidTattooStyles<T>(
        this IRuleBuilder<T, List<string>?> ruleBuilder) =>
        ruleBuilder
            .Must(styles => styles is null || styles.All(TattooStyle.All.Contains))
            .WithMessage($"Specializations must only contain: {string.Join(", ", TattooStyle.All)}.")
            .Must(styles => styles is null || styles.Distinct().Count() == styles.Count)
            .WithMessage("Specializations must not contain duplicate values.");
}
