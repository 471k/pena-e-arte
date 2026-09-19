using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Application.Services.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class ServiceHandlerIntegrationTests(DatabaseFixture fixture)
{
    // ── Tenant isolation — the exact class of bug an in-memory unit test can't catch,
    //    since it relies on AppDbContext's real, MySQL-backed global HasQueryFilter, not just
    //    the explicit IgnoreQueryFilters()-scoped predicates CreateAppointmentCoreAsync uses. ──

    [Fact]
    public async Task GetServices_ReturnsTenantScopedServicesOnly()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedService(tenantA, "A Service");
        await SeedService(tenantB, "B Service 1");
        await SeedService(tenantB, "B Service 2");

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetServicesHandler handler = new(db);
        List<ServiceResponse> result = await handler.Handle(new GetServicesQuery(), default);

        result.Should().ContainSingle(s => s.Name == "A Service");
    }

    [Fact]
    public async Task GetService_ServiceBelongingToAnotherTenant_ThrowsNotFoundException()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid otherTenantServiceId = await SeedService(tenantB, "Not Yours");

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetServiceHandler handler = new(db);

        Func<Task> act = () => handler.Handle(new GetServiceQuery(otherTenantServiceId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateService_ServiceBelongingToAnotherTenant_ThrowsNotFoundException()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid otherTenantServiceId = await SeedService(tenantB, "Not Yours");

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        UpdateServiceHandler handler = new(db);

        Func<Task> act = () => handler.Handle(
            new UpdateServiceCommand(otherTenantServiceId, new("Hijacked", null, 30, null, null, true)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── CreateService ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateService_ValidRequest_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreateServiceRequest req = new("New Tattoo Session", "Full session", 90, 150m, 50m, true);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreateServiceHandler handler = new(db, tenant);
        ServiceResponse result = await handler.Handle(new CreateServiceCommand(req), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        Service? service = await verify.Services.FindAsync(result.Id);
        service!.Name.Should().Be("New Tattoo Session");
        service.StudioId.Should().Be(tenantId);
    }

    [Fact]
    public async Task CreateService_MultipleActiveServices_AllRemainActive()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreateServiceHandler handler = new(db, tenant);

        await handler.Handle(new CreateServiceCommand(new("First", null, 60, null, null, true)), default);
        await handler.Handle(new CreateServiceCommand(new("Second", null, 60, null, null, true)), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        List<Service> services = await verify.Services.Where(s => s.StudioId == tenantId).ToListAsync();
        services.Should().HaveCount(2);
        services.Should().OnlyContain(s => s.IsActive);
    }

    // ── DeleteService ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteService_ValidId_SetsDeletedAtAndAppointmentKeepsServiceId()
    {
        Guid tenantId = Guid.NewGuid();
        Guid serviceId = await SeedService(tenantId, "To delete");

        await using AppDbContext appointmentCtx = fixture.CreateDbContext(tenantId);
        Client client = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@test.com" };
        appointmentCtx.Clients.Add(client);
        Appointment appointment = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            ServiceId = serviceId,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            DurationMinutes = 30,
        };
        appointmentCtx.Appointments.Add(appointment);
        await appointmentCtx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        DeleteServiceHandler handler = new(db);
        await handler.Handle(new DeleteServiceCommand(serviceId), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        Service? service = await verify.Services.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == serviceId);
        service!.DeletedAt.Should().NotBeNull();

        Appointment? persistedAppointment = await verify.Appointments.FirstOrDefaultAsync(a => a.Id == appointment.Id);
        persistedAppointment!.ServiceId.Should().Be(serviceId);
    }

    // ── CreateAppointmentCommand + Service — real-DB confirmation of the FK/query-filter
    //    wiring (ServiceConfiguration's FK, AppDbContext's HasQueryFilter pair). ──

    [Fact]
    public async Task CreateAppointment_WithServiceFromAnotherTenant_ThrowsNotFoundException()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid otherTenantServiceId = await SeedService(tenantB, "Not Yours", durationMinutes: 45);

        await using AppDbContext seedCtx = fixture.CreateDbContext(tenantA);
        Client client = new() { StudioId = tenantA, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@test.com" };
        seedCtx.Clients.Add(client);
        await seedCtx.SaveChangesAsync();

        // Service resolution runs before any artist-availability check in
        // CreateAppointmentCoreAsync, so this throws NotFoundException without needing an
        // artist/StudioHours seeded — the cross-tenant service lookup is what's under test.
        CreateAppointmentRequest req = new(
            null, client.Id, DateTime.UtcNow.AddDays(3), 90, null, ServiceId: otherTenantServiceId);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantA);
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.Role.Returns("artist");
        CreateAppointmentHandler handler = new(
            db, tenant, user,
            Substitute.For<ISlotLocker>(), Substitute.For<IJobScheduler>(), Substitute.For<IRealtimeNotifier>(),
            Substitute.For<ISender>(), Substitute.For<IPlanLimitService>());

        Func<Task> act = () => handler.Handle(new CreateAppointmentCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedService(Guid tenantId, string name, int durationMinutes = 60, bool isActive = true)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Service service = new() { StudioId = tenantId, Name = name, DurationMinutes = durationMinutes, IsActive = isActive };
        ctx.Services.Add(service);
        await ctx.SaveChangesAsync();
        return service.Id;
    }
}
