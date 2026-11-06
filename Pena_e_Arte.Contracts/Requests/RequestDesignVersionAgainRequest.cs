namespace Pena_e_Arte.Contracts.Requests;

/// <summary>POST designs/revisions/{revisionId}/request-again: the client asks for this earlier version back,
/// with an optional note for the artist (2026-11-06).</summary>
public record RequestDesignVersionAgainRequest(string? Notes);
