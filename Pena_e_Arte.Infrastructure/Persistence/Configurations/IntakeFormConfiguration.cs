using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class IntakeFormConfiguration : TenantEntityConfiguration<IntakeForm>
{
    protected override string TableName => "intake_forms";

    public override void Configure(EntityTypeBuilder<IntakeForm> builder)
    {
        base.Configure(builder);

        builder.Property(f => f.FormData).HasColumnType("longtext").IsRequired();
        builder.Property(f => f.FileUrl).HasMaxLength(1000);

        builder.HasOne(f => f.Client)
               .WithMany()
               .HasForeignKey(f => f.ClientId)
               .HasConstraintName("fk_intake_forms_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
