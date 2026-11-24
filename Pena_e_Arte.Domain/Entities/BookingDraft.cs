namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A signed-in client's unfinished booking request at one studio (S04, saved booking drafts). Phi's decisions
/// (24 Nov 2026 decision form and earlier): the draft keeps every field EXCEPT the safety notes ("Anything else I
/// should know?", which invites medical details), lives seven days from its last save, belongs to its author
/// only, and is deleted at once when that person's data is erased.
///
/// One draft per person per studio. The owner is the login (UserId), not a Client row, because a first-time
/// client may not have a Client row at this studio yet. Photos are referenced in BookingDraftAttachments, a
/// column the orphaned-object sweep reads, never inside PayloadJson.
/// </summary>
public class BookingDraft : TenantEntity
{
    /// <summary>How long a draft lives after its last save (Phi's decision: seven days).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>The field the draft must never hold, in the form's own naming.</summary>
    public const string ExcludedField = "safetyNotes";

    public Guid UserId { get; set; }

    /// <summary>The form's values as JSON, never including <see cref="ExcludedField"/>.</summary>
    public string PayloadJson { get; set; } = "{}";

    public DateTime ExpiresAt { get; set; }

    public List<BookingDraftAttachment> Attachments { get; set; } = [];
}
