namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// The aftercare steps the studio's aftercare email sends the client after a completed session
/// (EmailRenderer.RenderAftercare). This list is the only copy: the email and the client
/// appointment page (DEC11) both read it, so the two cannot drift. Change the advice here.
/// </summary>
public static class AftercareInstructions
{
    public static readonly IReadOnlyList<string> Steps =
    [
        "Keep the tattoo covered for 2–4 hours after the session.",
        "Gently wash with lukewarm water and fragrance-free soap.",
        "Pat dry — never rub.",
        "Apply a thin layer of unscented moisturizer 2–3 times daily for 2 weeks.",
        "Avoid sun exposure, pools, and soaking for 2 weeks.",
        "Contact the studio if you notice redness, swelling, or discharge after 3 days.",
    ];
}
