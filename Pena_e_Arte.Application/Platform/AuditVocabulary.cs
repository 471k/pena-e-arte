using System.Reflection;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.Platform;

/// <summary>
/// The audit log's named choices (D01): every action and target type the code can write, read from the
/// constants themselves so a new constant appears in the admin's filter without a second list to update.
/// </summary>
public static class AuditVocabulary
{
    public static readonly IReadOnlyList<string> Actions = ConstantsOf(typeof(AuditActions));
    public static readonly IReadOnlyList<string> TargetTypes = ConstantsOf(typeof(AuditTargetTypes));

    private static List<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
}
