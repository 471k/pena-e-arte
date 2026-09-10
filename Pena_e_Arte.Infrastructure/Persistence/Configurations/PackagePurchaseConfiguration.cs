using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PackagePurchaseConfiguration : TenantEntityConfiguration<PackagePurchase>
{
    protected override string TableName => "package_purchases";

    public override void Configure(EntityTypeBuilder<PackagePurchase> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.SessionsRemaining).IsRequired();
        builder.Property(p => p.ProviderReferenceId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.ClientSecret).HasMaxLength(500);
        builder.Property(p => p.Provider).HasMaxLength(32).IsRequired();

        builder.HasIndex(p => new { p.StudioId, p.ClientId });
        builder.HasIndex(p => p.ConfirmedAt);

        builder.HasOne(p => p.Package)
               .WithMany()
               .HasForeignKey(p => p.PackageId)
               .HasConstraintName("fk_package_purchases_packages")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Client)
               .WithMany()
               .HasForeignKey(p => p.ClientId)
               .HasConstraintName("fk_package_purchases_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
