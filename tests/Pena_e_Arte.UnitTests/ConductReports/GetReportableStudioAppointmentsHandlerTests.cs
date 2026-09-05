using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class GetReportableStudioAppointmentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetReportableStudioAppointmentsHandler CreateSut() => new(_db);

    private async Task<Studio> SeedStudio(string slug = "ink-studio")
    {
        Studio studio = new() { Id = Guid.NewGuid(), Name = "Ink Studio", Slug = slug, City = "Lisbon", IsActive = true };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio;
    }

    private async Task<Appointment> SeedAppointment(Guid studioId, Guid reporterUserId, AppointmentStatus status)
    {
        Client client = new()
        {
            StudioId = studioId,
            UserId = reporterUserId,
            FirstName = "Ana",
            LastName = "Silva",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId = studioId,
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(-5).AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Returns_non_completed_appointment()
    {
        Studio studio = await SeedStudio();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, reporterId, AppointmentStatus.Pending);

        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableStudioAppointmentsQuery(studio.Slug, reporterId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id && a.Status == "Pending");
    }

    [Fact]
    public async Task Includes_appointment_even_if_already_reported()
    {
        Studio studio = await SeedStudio();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, reporterId, AppointmentStatus.Completed);

        _db.ConductReports.Add(ConductReport.ForStudio(
            studio.Id, appt.Id, reporterId, "Ana Silva", ReportCategory.Other,
            "Already filed one report about this same appointment."));
        await _db.SaveChangesAsync();

        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableStudioAppointmentsQuery(studio.Slug, reporterId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id);
    }

    [Fact]
    public async Task Returns_empty_list_when_studio_not_found()
    {
        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableStudioAppointmentsQuery("nonexistent", Guid.NewGuid()), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
