using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.ConsentForms.Queries;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Queries;
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
public class FormHandlerIntegrationTests(DatabaseFixture fixture)
{
    private readonly ISender                _sender = Substitute.For<ISender>();
    private readonly IConsentFormPdfService _pdf    = Substitute.For<IConsentFormPdfService>();
    private readonly IR2Service             _r2     = Substitute.For<IR2Service>();

    // ── SubmitIntakeForm ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitIntakeForm_WithValidClient_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        SubmitIntakeFormHandler handler = new(db, TenantFor(tenantId), StaffUser(), _sender);

        IntakeFormResponse result = await handler.Handle(
            new SubmitIntakeFormCommand(new SubmitIntakeFormRequest(clientId, null, "{\"allergies\":\"none\"}", null)),
            default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.IntakeForms.AnyAsync(f => f.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitIntakeForm_FromDifferentTenant_NotVisibleToOtherTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantA);

        await using AppDbContext dbA = fixture.CreateDbContext(tenantA);
        IntakeFormResponse result = await new SubmitIntakeFormHandler(dbA, TenantFor(tenantA), StaffUser(), _sender)
            .Handle(new SubmitIntakeFormCommand(new SubmitIntakeFormRequest(clientId, null, "{}", null)), default);

        await using AppDbContext dbB = fixture.CreateDbContext(tenantB);
        bool visible = await dbB.IntakeForms.AnyAsync(f => f.Id == result.Id);
        visible.Should().BeFalse(because: "the query filter prevents tenantB from seeing tenantA's forms");
    }

    // ── GetIntakeForms ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIntakeForms_FilterByClientId_ReturnsOnlyMatchingForms()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientA  = await SeedClient(tenantId);
        Guid clientB  = await SeedClient(tenantId);

        await SubmitForm(tenantId, clientA, "{\"a\":1}");
        await SubmitForm(tenantId, clientB, "{\"b\":2}");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        List<IntakeFormResponse> result = await new GetIntakeFormsHandler(db, StaffUser())
            .Handle(new GetIntakeFormsQuery(clientA, null), default);

        result.Should().ContainSingle(f => f.ClientId == clientA);
    }

    // ── GetIntakeFormById ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIntakeFormById_ExistingId_ReturnsForm()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId);
        IntakeFormResponse created = await SubmitForm(tenantId, clientId, "{}");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        IntakeFormResponse result = await new GetIntakeFormByIdHandler(db, StaffUser())
            .Handle(new GetIntakeFormByIdQuery(created.Id), default);

        result.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetIntakeFormById_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Func<Task> act = () => new GetIntakeFormByIdHandler(db, StaffUser())
            .Handle(new GetIntakeFormByIdQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── SignConsentForm ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SignConsentForm_ValidRequest_PersistsToDatabase()
    {
        Guid tenantId     = Guid.NewGuid();
        (Guid clientId, Guid appointmentId) = await SeedClientAndAppointment(tenantId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        ConsentFormResponse result = await new SignConsentFormHandler(db, TenantFor(tenantId), StaffUser(), _pdf, _r2, _sender)
            .Handle(new SignConsentFormCommand(
                new SignConsentFormRequest(clientId, appointmentId, "data:image/png;base64,abc")), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.ConsentForms.AnyAsync(f => f.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SignConsentForm_SameAppointmentTwice_ThrowsConsentFormAlreadySignedException()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid appointmentId) = await SeedClientAndAppointment(tenantId);

        await SignConsent(tenantId, clientId, appointmentId, "sig1");

        Func<Task> act = () => SignConsent(tenantId, clientId, appointmentId, "sig2");

        await act.Should().ThrowAsync<ConsentFormAlreadySignedException>();
    }

    [Fact]
    public async Task SignConsentForm_FromDifferentTenant_NotVisibleToOtherTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        (Guid clientId, Guid appointmentId) = await SeedClientAndAppointment(tenantA);

        ConsentFormResponse result = await SignConsent(tenantA, clientId, appointmentId, "sig");

        await using AppDbContext dbB = fixture.CreateDbContext(tenantB);
        bool visible = await dbB.ConsentForms.AnyAsync(f => f.Id == result.Id);
        visible.Should().BeFalse(because: "the query filter prevents tenantB from seeing tenantA's consent forms");
    }

    // ── GetConsentForms ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConsentForms_FilterByAppointmentId_ReturnsOnlyMatchingForms()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientA, Guid apptA) = await SeedClientAndAppointment(tenantId);
        (Guid clientB, Guid apptB) = await SeedClientAndAppointment(tenantId);

        await SignConsent(tenantId, clientA, apptA, "sig-a");
        await SignConsent(tenantId, clientB, apptB, "sig-b");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        List<ConsentFormResponse> result = await new GetConsentFormsHandler(db, StaffUser())
            .Handle(new GetConsentFormsQuery(null, apptA), default);

        result.Should().ContainSingle(f => f.AppointmentId == apptA);
    }

    // ── GetConsentFormById ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConsentFormById_ExistingId_ReturnsForm()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid appointmentId) = await SeedClientAndAppointment(tenantId);
        ConsentFormResponse created = await SignConsent(tenantId, clientId, appointmentId, "sig");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        ConsentFormDetailResponse result = await new GetConsentFormByIdHandler(db, StaffUser(), NullLogger<GetConsentFormByIdHandler>.Instance)
            .Handle(new GetConsentFormByIdQuery(created.Id), default);

        result.Id.Should().Be(created.Id);
        result.ClientName.Should().Be("Jane Doe");
        result.ArtistName.Should().Be("Art ist");
    }

    [Fact]
    public async Task GetConsentFormById_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Func<Task> act = () => new GetConsentFormByIdHandler(db, StaffUser(), NullLogger<GetConsentFormByIdHandler>.Instance)
            .Handle(new GetConsentFormByIdQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedClient(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId  = tenantId,
            FirstName = "Jane",
            LastName  = "Doe",
            Email     = $"{Guid.NewGuid()}@test.com"
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return client.Id;
    }

    private async Task<(Guid ClientId, Guid AppointmentId)> SeedClientAndAppointment(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId  = tenantId,
            FirstName = "Jane",
            LastName  = "Doe",
            Email     = $"{Guid.NewGuid()}@test.com"
        };
        Artist artist = new()
        {
            StudioId  = tenantId,
            FirstName = "Art",
            LastName  = "ist",
            Email     = $"{Guid.NewGuid()}@test.com"
        };
        ctx.Clients.Add(client);
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId        = tenantId,
            ClientId        = client.Id,
            ArtistId        = artist.Id,
            Date            = DateTime.UtcNow.AddDays(7),
            EndDate         = DateTime.UtcNow.AddDays(7).AddHours(2),
            DurationMinutes = 120
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();

        return (client.Id, appointment.Id);
    }

    private async Task<IntakeFormResponse> SubmitForm(Guid tenantId, Guid clientId, string formData)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        return await new SubmitIntakeFormHandler(db, TenantFor(tenantId), StaffUser(), _sender)
            .Handle(new SubmitIntakeFormCommand(new SubmitIntakeFormRequest(clientId, null, formData, null)), default);
    }

    private async Task<ConsentFormResponse> SignConsent(
        Guid tenantId, Guid clientId, Guid appointmentId, string signature)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        return await new SignConsentFormHandler(db, TenantFor(tenantId), StaffUser(), _pdf, _r2, _sender)
            .Handle(new SignConsentFormCommand(
                new SignConsentFormRequest(clientId, appointmentId, signature)), default);
    }

    private static ICurrentTenant TenantFor(Guid tenantId)
    {
        CurrentTenantService t = new();
        t.SetTenant(tenantId);
        return t;
    }

    private static ICurrentUser StaffUser() => new StubCurrentUser(Guid.NewGuid(), "artist");

    private sealed record StubCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
