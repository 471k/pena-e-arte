using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class IntakeFormTemplateConfiguration : TenantEntityConfiguration<IntakeFormTemplate>
{
    protected override string TableName => "intake_form_templates";

    public override void Configure(EntityTypeBuilder<IntakeFormTemplate> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.FieldSchemaJson).IsRequired().HasColumnType("text");
    }
}
