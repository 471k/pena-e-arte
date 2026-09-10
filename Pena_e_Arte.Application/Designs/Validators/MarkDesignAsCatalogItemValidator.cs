using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class MarkDesignAsCatalogItemValidator : AbstractValidator<MarkDesignAsCatalogItemCommand>
{
    public MarkDesignAsCatalogItemValidator()
    {
        RuleFor(x => x.DesignId).NotEmpty();

        RuleFor(x => x.Request.Price)
            .GreaterThan(0)
            .When(x => x.Request.IsCatalogItem && x.Request.Price.HasValue);
    }
}
