using System.Text.Json;
using FluentValidation;
using Pena_e_Arte.Application.IntakeForms.Commands;

namespace Pena_e_Arte.Application.IntakeForms.Validators;

public class UpsertIntakeFormTemplateValidator : AbstractValidator<UpsertIntakeFormTemplateCommand>
{
    private const int MaxFields = 20;

    public UpsertIntakeFormTemplateValidator()
    {
        RuleFor(x => x.Request.FieldSchemaJson)
            .NotEmpty()
            .Must(BeAValidFieldSchema)
            .WithMessage($"FieldSchemaJson must be a JSON array of 1-{MaxFields} fields, each with a " +
                         $"non-empty label and a type of Text/Textarea/Select/Checkbox/Date.");
    }

    private static bool BeAValidFieldSchema(string json)
    {
        List<IntakeFormFieldDefinition>? fields;
        try
        {
            fields = JsonSerializer.Deserialize<List<IntakeFormFieldDefinition>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return false;
        }

        if (fields is null || fields.Count == 0 || fields.Count > MaxFields) return false;

        return fields.All(f =>
            !string.IsNullOrWhiteSpace(f.Label) &&
            IntakeFormFieldTypes.Allowed.Contains(f.Type));
    }
}
