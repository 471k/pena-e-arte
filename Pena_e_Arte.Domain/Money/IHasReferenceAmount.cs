namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// A client money record that can carry the lek reference frozen when the client paid (Albanian EUR
/// studios only, docs/specs/spec-eur-with-lek-display-2026-10-26.md D3/D5). All five are null when no
/// reference applies or no rate existed; once set they are never recomputed from a later rate.
/// </summary>
public interface IHasReferenceAmount
{
    /// <summary>"ALL" when a reference was frozen.</summary>
    string? ReferenceCurrency { get; set; }
    /// <summary>Lek per euro used, 6 dp, e.g. 90.740000.</summary>
    decimal? ReferenceRate { get; set; }
    /// <summary>The date the source states for <see cref="ReferenceRate"/>.</summary>
    DateOnly? ReferenceRateDate { get; set; }
    /// <summary>For example "BoA-Fix".</summary>
    string? ReferenceRateSource { get; set; }
    /// <summary>The record's euro amount in whole lek (ReferenceAmount.ToWholeLek).</summary>
    decimal? ReferenceAmount { get; set; }
}
