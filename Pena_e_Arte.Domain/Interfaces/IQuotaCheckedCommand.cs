using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Marks a MediatR command whose execution must be checked against the current studio's
/// Plan limits before it runs. Picked up by PlanLimitBehavior in the Application layer's
/// MediatR pipeline (registered alongside ValidationBehavior in Program.cs).
/// See docs/claude/architecture.md Decisions Log — "Plan usage limits".
/// </summary>
public interface IQuotaCheckedCommand
{
    QuotaType QuotaType { get; }
}
