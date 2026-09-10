using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class UpsertIntakeFormTemplateValidatorTests
{
    private readonly UpsertIntakeFormTemplateValidator _validator = new();

    private static UpsertIntakeFormTemplateCommand Command(string json, bool isActive = true) =>
        new(new UpsertIntakeFormTemplateRequest(json, isActive));

    [Fact]
    public void Validate_SingleTextField_Passes()
    {
        _validator.ShouldBeValid(Command(
            """[{"label":"Allergies","type":"Text","required":true}]"""));
    }

    [Fact]
    public void Validate_AllFiveFieldTypes_Passes()
    {
        _validator.ShouldBeValid(Command(
            """
            [
              {"label":"Name","type":"Text","required":true},
              {"label":"Notes","type":"Textarea","required":false},
              {"label":"Skin type","type":"Select","required":true,"options":["Oily","Dry"]},
              {"label":"Consent","type":"Checkbox","required":true},
              {"label":"DOB","type":"Date","required":true}
            ]
            """));
    }

    [Fact]
    public void Validate_EmptyJson_FailsOnFieldSchemaJson()
    {
        _validator.ShouldFailOn(Command(""), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_EmptyArray_FailsOnFieldSchemaJson()
    {
        _validator.ShouldFailOn(Command("[]"), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_MalformedJson_FailsOnFieldSchemaJson()
    {
        _validator.ShouldFailOn(Command("not json"), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_UnknownFieldType_FailsOnFieldSchemaJson()
    {
        _validator.ShouldFailOn(Command(
            """[{"label":"Signature","type":"Signature","required":true}]"""), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_EmptyLabel_FailsOnFieldSchemaJson()
    {
        _validator.ShouldFailOn(Command(
            """[{"label":"","type":"Text","required":true}]"""), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_MoreThanTwentyFields_FailsOnFieldSchemaJson()
    {
        string fields = string.Join(",", Enumerable.Range(1, 21)
            .Select(i => $$"""{"label":"Field {{i}}","type":"Text","required":false}"""));
        _validator.ShouldFailOn(Command($"[{fields}]"), "Request.FieldSchemaJson");
    }

    [Fact]
    public void Validate_ExactlyTwentyFields_Passes()
    {
        string fields = string.Join(",", Enumerable.Range(1, 20)
            .Select(i => $$"""{"label":"Field {{i}}","type":"Text","required":false}"""));
        _validator.ShouldBeValid(Command($"[{fields}]"));
    }
}
