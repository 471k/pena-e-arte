namespace Pena_e_Arte.Contracts.Requests;

/// <summary>The claim token from the waitlist email's link (C09), posted in the body so no URL carries it.</summary>
public record WaitlistClaimRequest(string Token);
