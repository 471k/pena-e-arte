namespace Pena_e_Arte.Contracts.Requests;

/// <summary>C05: the token from a "booking attempt" email's resume link, posted in the body (the link keeps it in
/// the URL fragment, which no server ever sees).</summary>
public record ResumeBookingDraftRequest(string Token);
