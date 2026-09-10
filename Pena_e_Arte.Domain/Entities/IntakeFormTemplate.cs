namespace Pena_e_Arte.Domain.Entities;

public class IntakeFormTemplate : TenantEntity
{
    public bool IsActive { get; set; }

    /// <summary>JSON array of field definitions: {label, type, required, options?}.
    /// type is one of Text/Textarea/Select/Checkbox/Date — see IntakeFormFieldType.</summary>
    public string FieldSchemaJson { get; set; } = string.Empty;
}
