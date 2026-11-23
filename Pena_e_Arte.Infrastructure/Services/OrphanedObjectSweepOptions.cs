namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// The orphaned-object sweep's switch (docs/infra/orphan-quarantine-runbook.md). Off by default: with no
/// value, or any value other than "Quarantine", the job is report-only exactly as in Phase 2. There is no
/// hard-delete mode: that is Phase 4, a separate decision.
/// Set per environment as OrphanedObjectSweep__Mode=Quarantine, and only after the _quarantine/ lock rule
/// exists on that environment's bucket (the runbook's order).
/// </summary>
public class OrphanedObjectSweepOptions
{
    public const string Section = "OrphanedObjectSweep";

    public string? Mode { get; init; }
}
