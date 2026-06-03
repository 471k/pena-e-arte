namespace Pena_e_Arte.Contracts.Requests;

public record SessionSplitItem(string Label, decimal Amount);

public record UpdateSessionSplitsRequest(IReadOnlyList<SessionSplitItem> Splits);
