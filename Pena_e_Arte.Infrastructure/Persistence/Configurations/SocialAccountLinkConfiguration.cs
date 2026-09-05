using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SocialAccountLinkConfiguration : TenantEntityConfiguration<SocialAccountLink>
{
    protected override string TableName => "social_account_links";

    public override void Configure(EntityTypeBuilder<SocialAccountLink> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.SubjectType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.Platform).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.VerificationMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Handle).HasMaxLength(60).IsRequired();
        builder.Property(s => s.EncryptedToken).HasColumnType("TEXT");
        builder.Property(s => s.PendingVerificationCode).HasMaxLength(32);

        builder.HasIndex(s => new { s.SubjectType, s.SubjectId, s.Platform })
               .IsUnique()
               .HasDatabaseName("ix_social_account_links_subject_platform");

        // No HasOne/WithMany navigation — SubjectId is polymorphic (Artist or Studio),
        // unlike InstagramConnection's single-subject direct FK. No global query
        // filter either — same documented exception as InstagramConnection (see
        // AppDbContext and SocialAccountLink's own doc comment); handlers must filter
        // by (SubjectType, SubjectId) and verify tenant ownership explicitly.
    }
}
