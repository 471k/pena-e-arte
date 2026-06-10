# Database Instructions — MySQL 8.4 + EF Core 10

> Load this file when working on entities, migrations, DbContext,
> or anything in `Pena_e_Arte.Infrastructure/Persistence/`.

---

## DbContext Setup

The example below is the full expected DbSet list. Add a DbSet and query filter
for every new tenant-scoped entity. Issuer-level entities (Plan, Subscription, Studio)
are NOT query-filtered.

```csharp
// Infrastructure/Persistence/AppDbContext.cs
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentTenant tenant) : DbContext(options)
{
    // --- Tenant-scoped ---
    public DbSet<Appointment>    Appointments    => Set<Appointment>();
    public DbSet<DepositRule>    DepositRules    => Set<DepositRule>();
    public DbSet<Client>         Clients         => Set<Client>();
    public DbSet<ClientProfile>  ClientProfiles  => Set<ClientProfile>();
    public DbSet<TattooRecord>   TattooRecords   => Set<TattooRecord>();
    public DbSet<Artist>         Artists         => Set<Artist>();
    public DbSet<Design>         Designs         => Set<Design>();
    public DbSet<DesignRevision> DesignRevisions => Set<DesignRevision>();
    public DbSet<DesignApproval> DesignApprovals => Set<DesignApproval>();
    public DbSet<Payment>        Payments        => Set<Payment>();
    public DbSet<SessionSplit>   SessionSplits   => Set<SessionSplit>();
    public DbSet<IntakeForm>     IntakeForms     => Set<IntakeForm>();
    public DbSet<ConsentForm>    ConsentForms    => Set<ConsentForm>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    // --- Issuer-level (no tenant filter) ---
    public DbSet<Studio>       Studios       => Set<Studio>();
    public DbSet<Plan>         Plans         => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tenant-scoped query filters
        builder.Entity<Appointment>()   .HasQueryFilter(a => a.StudioId == tenant.StudioId);
        builder.Entity<DepositRule>()   .HasQueryFilter(d => d.StudioId == tenant.StudioId);
        builder.Entity<Client>()        .HasQueryFilter(c => c.StudioId == tenant.StudioId);
        builder.Entity<ClientProfile>() .HasQueryFilter(c => c.StudioId == tenant.StudioId);
        builder.Entity<TattooRecord>()  .HasQueryFilter(t => t.StudioId == tenant.StudioId);
        builder.Entity<Artist>()        .HasQueryFilter(a => a.StudioId == tenant.StudioId);
        builder.Entity<Design>()        .HasQueryFilter(d => d.StudioId == tenant.StudioId);
        builder.Entity<DesignRevision>().HasQueryFilter(d => d.StudioId == tenant.StudioId);
        builder.Entity<DesignApproval>().HasQueryFilter(d => d.StudioId == tenant.StudioId);
        builder.Entity<Payment>()       .HasQueryFilter(p => p.StudioId == tenant.StudioId);
        builder.Entity<SessionSplit>()  .HasQueryFilter(s => s.StudioId == tenant.StudioId);
        builder.Entity<IntakeForm>()    .HasQueryFilter(i => i.StudioId == tenant.StudioId);
        builder.Entity<ConsentForm>()   .HasQueryFilter(c => c.StudioId == tenant.StudioId);
        builder.Entity<NotificationLog>().HasQueryFilter(n => n.StudioId == tenant.StudioId);

        // Issuer-level — NOT filtered
        // Studio, Plan, Subscription: IgnoreQueryFilters() not needed, no filter applied
    }
}
```

---

## Studio Entity Fields

All fields the Studio entity must carry (scattered across architecture.md — consolidated here):

```csharp
public class Studio  // NOT a TenantEntity — issuer-owned
{
    public Guid     Id               { get; init; } = Guid.NewGuid();
    public string   Name             { get; set; }
    public string   Slug             { get; set; }  // url-safe unique identifier
    public string   City             { get; set; }
    public double   Latitude         { get; set; }
    public double   Longitude        { get; set; }
    public bool     IsActive         { get; set; }
    public DateTime TrialExpiresAt   { get; set; }  // CreatedAt + 14 days
    public string?  StripeAccountId  { get; set; }  // Stripe Connect
    public DateTime CreatedAt        { get; init; } = DateTime.UtcNow;
}
```

---

## Entity Configuration Pattern

Never put EF attributes on domain entities. Use `IEntityTypeConfiguration`.

```csharp
// Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs
public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.StudioId).IsRequired();
        builder.Property(a => a.Status)
               .HasConversion<string>()
               .HasMaxLength(32);

        builder.HasIndex(a => new { a.StudioId, a.ArtistId, a.Date })
               .HasDatabaseName("ix_appointments_studio_artist_date");

        builder.HasOne(a => a.Artist)
               .WithMany(ar => ar.Appointments)
               .HasForeignKey(a => a.ArtistId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
```

---

## Naming Conventions (MySQL)

| Object | Convention | Example |
|---|---|---|
| Tables | snake_case, plural | `appointments` |
| Columns | snake_case | `studio_id`, `created_at` |
| Indexes | `ix_table_columns` | `ix_appointments_studio_artist_date` |
| FK constraints | `fk_table_reference` | `fk_appointments_artists` |
| Primary keys | `pk_table` | `pk_appointments` |

---

## Tenant Isolation Rules

**Query with filter (default — all tenant-scoped entities):**
```csharp
// Tenant filter applied automatically
var appointments = await _db.Appointments.ToListAsync(ct);
```

**Query without filter (`issuer` role cross-tenant queries only):**
```csharp
// Only use IgnoreQueryFilters in IssuerOnly-authorized handlers
var allAppointments = await _db.Appointments
    .IgnoreQueryFilters()
    .Where(a => a.Date >= from)
    .ToListAsync(ct);
```

Never call `IgnoreQueryFilters()` in a handler that is not behind `IssuerOnly` policy.

---

## Base Entity

All tenant-scoped entities inherit from this:

```csharp
// Domain/Entities/TenantEntity.cs
public abstract class TenantEntity
{
    public Guid      Id        { get; init; } = Guid.NewGuid();
    public Guid      StudioId  { get; set; }  // tenant key
    public DateTime  CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime  UpdatedAt { get; set; }  = DateTime.UtcNow;
}
```

---

## Migrations

Always name migrations descriptively.

```bash
# Add migration
dotnet ef migrations add AddAppointmentDepositColumn \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API

# Apply
dotnet ef database update \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

**Zero-downtime migration order for breaking changes:**
1. Add new nullable column → migrate
2. Deploy code writing to both columns
3. Backfill data
4. Deploy code reading from new column
5. Make column non-nullable → migrate
6. Remove old column in a later release

---

## Soft Deletes

Tenant data is soft-deleted, never hard-deleted (GDPR right to erasure is
handled by a separate purge pipeline, not by direct DELETE).

```csharp
// Add to TenantEntity
public DateTime? DeletedAt { get; set; }

// Add to query filters
builder.Entity<Client>().HasQueryFilter(c =>
    c.StudioId == tenant.StudioId && c.DeletedAt == null);
```

---

## Useful Queries

```csharp
// Check slot availability (accounts for buffer time)
var conflict = await _db.Appointments
    .Where(a =>
        a.ArtistId == artistId &&
        a.Date < requestEnd &&
        a.EndDate > requestStart &&
        a.Status != AppointmentStatus.Cancelled)
    .AnyAsync(ct);

// Paginated list — always cursor-based, never offset
var results = await _db.Appointments
    .Where(a => a.Id > lastSeenId)
    .OrderBy(a => a.Date)
    .Take(pageSize)
    .ToListAsync(ct);
```

---

## Redis Patterns

```csharp
// Appointment slot lock (prevent double-booking race condition)
var lockKey = $"slot:{studioId}:{artistId}:{date:yyyyMMddHHmm}";
var acquired = await _cache.SetAsync(lockKey, "1",
    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
    when: When.NotExists);

if (!acquired) throw new SlotAlreadyBookedException();

// Session store key convention
// session:{userId}
// ratelimit:{tenantId}:{endpoint}
// slot:{studioId}:{artistId}:{datetime}
```
