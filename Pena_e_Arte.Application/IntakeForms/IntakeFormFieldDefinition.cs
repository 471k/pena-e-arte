namespace Pena_e_Arte.Application.IntakeForms;

/// <summary>One field in an owner-configured IntakeFormTemplate.FieldSchemaJson array.</summary>
public record IntakeFormFieldDefinition(string Label, string Type, bool Required, List<string>? Options);

public static class IntakeFormFieldTypes
{
    public const string Text = "Text";
    public const string Textarea = "Textarea";
    public const string Select = "Select";
    public const string Checkbox = "Checkbox";
    public const string Date = "Date";

    public static readonly HashSet<string> Allowed = [Text, Textarea, Select, Checkbox, Date];
}
