using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class BookingIntakeConfiguration : TenantEntityConfiguration<BookingIntake>
{
    protected override string TableName => "booking_intakes";

    public override void Configure(EntityTypeBuilder<BookingIntake> builder)
    {
        base.Configure(builder);

        builder.Property(i => i.TattooDescription).IsRequired().HasMaxLength(4000);
        builder.Property(i => i.SafetyNotes).HasMaxLength(4000);
        builder.Property(i => i.ReferralSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.ReferralSourceOther).HasMaxLength(200);

        builder.Property(i => i.DesiredPlacement)
               .HasColumnName("desired_placement")
               .HasColumnType("json")
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<BodyMap>(v, (JsonSerializerOptions?)null) ?? new BodyMap()
               )
               .Metadata.SetValueComparer(new ValueComparer<BodyMap>(
                   (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                          == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                   v => JsonSerializer.Deserialize<BodyMap>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null) ?? new BodyMap()
               ));

        builder.HasOne(i => i.Appointment)
               .WithOne(a => a.Intake)
               .HasForeignKey<BookingIntake>(i => i.AppointmentId)
               .HasConstraintName("fk_booking_intakes_appointments")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.AppointmentId)
               .IsUnique()
               .HasDatabaseName("ix_booking_intakes_appointment_id");
    }
}
