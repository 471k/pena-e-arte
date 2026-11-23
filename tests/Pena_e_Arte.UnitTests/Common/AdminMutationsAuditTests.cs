using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Common;

/// <summary>
/// The five admin mutations that wrote no audit entry before 23 Nov (D04 follow-up), named positively so the
/// list can be checked off: generate a referral code for a studio, create a plan, delete a plan, queue an
/// industry report, change a feedback report's status or internal note. Each test runs the real handler
/// through the real AuditLogBehavior.
/// </summary>
public class AdminMutationsAuditTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _admin = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _adminId = Guid.NewGuid();

    public AdminMutationsAuditTests()
    {
        _admin.UserId.Returns(_adminId);
        _admin.Role.Returns("admin");
        _tenant.IsSet.Returns(false);
    }

    private Task<TResponse> ThroughAudit<TRequest, TResponse>(TRequest command, Func<Task<TResponse>> handle)
        where TRequest : notnull =>
        new AuditLogBehavior<TRequest, TResponse>(_db, _admin, _tenant).Handle(command, _ => handle(), CancellationToken.None);

    private Task ThroughAudit<TRequest>(TRequest command, Func<Task> handle) where TRequest : notnull =>
        ThroughAudit<TRequest, Unit>(command, async () => { await handle(); return Unit.Value; });

    private AuditLogEntry SingleEntry(string action) => _db.AuditLogEntries.Should().ContainSingle(e => e.Action == action).Subject;

    [Fact]
    public void AllFive_AreAuditable()
    {
        Type[] commands =
        [
            typeof(AdminGenerateReferralCodeCommand), typeof(CreatePlanCommand), typeof(DeletePlanCommand),
            typeof(TriggerIndustryReportCommand), typeof(UpdateFeedbackStatusCommand),
        ];

        commands.Should().OnlyContain(t => typeof(IAuditableCommand).IsAssignableFrom(t));
    }

    [Fact]
    public async Task GenerateReferralCode_TargetsTheNewCode_UnderItsStudio_WithoutTheCode()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "S", Slug = "s-" + Guid.NewGuid().ToString("N")[..10], City = "Tirana", OwnerEmail = "o@test.com", IsActive = true });
        _db.ReferralCodes.Add(new ReferralCode { StudioId = studioId, Code = "OLD12345", IsActive = true });
        await _db.SaveChangesAsync();
        AdminGenerateReferralCodeCommand command = new(studioId);

        PlatformReferralCodeResponse created = await ThroughAudit(command, () =>
            new AdminGenerateReferralCodeHandler(_db, Substitute.For<IRealtimeNotifier>(), NullLogger<AdminGenerateReferralCodeHandler>.Instance)
                .Handle(command, default));

        AuditLogEntry entry = SingleEntry(AuditActions.ReferralCodeGeneratedByAdmin);
        entry.TargetType.Should().Be(AuditTargetTypes.ReferralCode);
        entry.TargetId.Should().Be(created.Id);
        entry.StudioId.Should().Be(studioId);
        entry.ActorUserId.Should().Be(_adminId);
        entry.Metadata.Should().Contain("\"deactivatedCount\":1").And.NotContain(created.Code);
    }

    [Fact]
    public async Task CreatePlan_TargetsTheNewPlan()
    {
        CreatePlanCommand command = new(new CreatePlanRequest("Studio Pro", 17, [new PlanPriceRequest("Monthly", 49m)]));

        PlanResponse created = await ThroughAudit(command, () =>
            new CreatePlanHandler(_db, Substitute.For<IStripeBillingService>()).Handle(command, default));

        AuditLogEntry entry = SingleEntry(AuditActions.PlanCreated);
        entry.TargetType.Should().Be(AuditTargetTypes.Plan);
        entry.TargetId.Should().Be(created.Id).And.NotBe(Guid.Empty);
        entry.Metadata.Should().Be("{\"planName\":\"Studio Pro\"}");
    }

    [Fact]
    public async Task DeletePlan_KeepsTheDeletedPlansName()
    {
        Plan plan = new() { Name = "Legacy" };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        DeletePlanCommand command = new(plan.Id);

        await ThroughAudit(command, () => new DeletePlanHandler(_db).Handle(command, default));

        AuditLogEntry entry = SingleEntry(AuditActions.PlanDeleted);
        entry.TargetId.Should().Be(plan.Id);
        entry.Metadata.Should().Be("{\"planName\":\"Legacy\"}");
    }

    // A refused delete (the plan has subscriptions) throws, so nothing is recorded as deleted.
    [Fact]
    public async Task ARefusedDelete_WritesNoEntry()
    {
        Plan plan = new() { Name = "In use" };
        _db.Plans.Add(plan);
        _db.Subscriptions.Add(new Subscription { StudioId = Guid.NewGuid(), PlanId = plan.Id });
        await _db.SaveChangesAsync();
        DeletePlanCommand command = new(plan.Id);

        Func<Task> act = () => ThroughAudit(command, () => new DeletePlanHandler(_db).Handle(command, default));

        await act.Should().ThrowAsync<Exception>();
        _db.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task TriggerIndustryReport_IsAPlatformWideEntry()
    {
        TriggerIndustryReportCommand command = new();

        await ThroughAudit(command, () =>
            new TriggerIndustryReportHandler(Substitute.For<IJobScheduler>(), NullLogger<TriggerIndustryReportHandler>.Instance)
                .Handle(command, default));

        AuditLogEntry entry = SingleEntry(AuditActions.IndustryReportTriggered);
        entry.TargetType.Should().Be(AuditTargetTypes.Platform);
        entry.TargetId.Should().Be(Guid.Empty);
        entry.StudioId.Should().BeNull();
    }

    // The condition Desktop set: the entry records THAT the internal note changed, never its words. The note is
    // the field behind the AdminNote leak (#371). The raw metadata is searched for the note's text itself, not
    // only for a field name, because the leak to guard against is the words.
    [Fact]
    public async Task FeedbackStatus_RecordsThatTheNoteChanged_NeverItsText()
    {
        FeedbackReport report = FeedbackReport.Create(Guid.NewGuid(), Guid.NewGuid(), "owner", "Studio", FeedbackType.General, "Title", "Body text here.");
        _db.FeedbackReports.Add(report);
        await _db.SaveChangesAsync();
        const string note = "Spoke to Mira Hoxha about the double charge";

        List<AuditLogEntry> entries = [];
        async Task Update(string? adminNote)
        {
            UpdateFeedbackStatusCommand command = new(report.Id, new UpdateFeedbackStatusRequest("Reviewing", adminNote));
            await ThroughAudit(command, () => new UpdateFeedbackStatusHandler(_db).Handle(command, default));
            entries.Add(_db.AuditLogEntries.AsEnumerable().Single(e => e.Action == AuditActions.FeedbackStatusUpdated && !entries.Contains(e)));
        }

        await Update(note);
        await Update(note);          // same note again: status only
        await Update("  " + note);   // differs only by whitespace the entity trims: not a change

        entries.Should().HaveCount(3);
        entries.Should().OnlyContain(e =>
            e.TargetType == AuditTargetTypes.FeedbackReport && e.TargetId == report.Id && e.StudioId == report.StudioId);
        entries.Select(e => JsonDocument.Parse(e.Metadata).RootElement.GetProperty("adminNoteChanged").GetBoolean())
            .Should().Equal(true, false, false);
        entries.Should().OnlyContain(e => !e.Metadata.Contains("Mira") && !e.Metadata.Contains("double charge"));
        entries[0].Metadata.Should().Be("{\"status\":\"Reviewing\",\"adminNoteChanged\":true}");
    }

    [Fact]
    public void FeedbackMetadata_NeverSerializesTheNote_EvenWithoutTheHandler()
    {
        string metadata = AuditMetadataBuilder.Build(
            new UpdateFeedbackStatusCommand(Guid.NewGuid(), new UpdateFeedbackStatusRequest("Resolved", "private words")));

        metadata.Should().NotContain("private words");
    }
}
