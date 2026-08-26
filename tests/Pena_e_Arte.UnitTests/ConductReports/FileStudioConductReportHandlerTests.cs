using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class FileStudioConductReportHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private FileStudioConductReportHandler CreateSut() =>
        new(_db, _notifications, NullLogger<FileStudioConductReportHandler>.Instance);

    private async Task<Studio> SeedStudio(string slug = "ink-studio")
    {
        Studio studio = new()
        {
            Id = Guid.NewGuid(),
            Name = "Ink Studio",
            Slug = slug,
            City = "Lisbon",
            IsActive = true,
            OwnerEmail = "owner@ink-studio.test",
        };
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
            Date = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-2).AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Handle_ValidAppointment_CreatesReport()
    {
        Studio studio = await SeedStudio();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, reporterId, AppointmentStatus.Completed);

        FileStudioConductReportCommand command = new(
            studio.Slug, appt.Id, reporterId, "Ana Silva", ReportCategory.UnsafeHygienePractices,
            "The studio floor and equipment did not look properly sanitized.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        _db.ConductReports.Should().ContainSingle(r =>
            r.ArtistId == null &&
            r.StudioId == studio.Id &&
            r.AppointmentId == appt.Id &&
            r.ReporterUserId == reporterId);
    }

    [Fact]
    public async Task Handle_NonCompletedAppointment_StillSucceeds()
    {
        Studio studio = await SeedStudio();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, reporterId, AppointmentStatus.Pending);

        FileStudioConductReportCommand command = new(
            studio.Slug, appt.Id, reporterId, "Ana Silva", ReportCategory.Discrimination,
            "I was refused service on a discriminatory basis during check-in.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        _db.ConductReports.Should().ContainSingle(r => r.AppointmentId == appt.Id);
    }

    [Fact]
    public async Task Handle_AppointmentBelongsToDifferentStudio_ThrowsNotFoundException()
    {
        Studio studioA = await SeedStudio("studio-a");
        Studio studioB = await SeedStudio("studio-b");
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studioA.Id, reporterId, AppointmentStatus.Completed);

        FileStudioConductReportCommand command = new(
            studioB.Slug, appt.Id, reporterId, "Ana Silva", ReportCategory.Other,
            "Wrong studio slug used for this appointment.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        FileStudioConductReportCommand command = new(
            "nonexistent-slug", Guid.NewGuid(), Guid.NewGuid(), "Someone", ReportCategory.Other,
            "Reporting a studio that does not exist.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
