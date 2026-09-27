namespace Pena_e_Arte.Contracts.Requests;

// Currency deliberately removed (was an unvalidated field the caller could set arbitrarily,
// disconnected from what actually got charged — see docs/claude/architecture.md Decisions Log,
// "Studio currency"). The studio's own Currency is always used instead.
public record CreatePaymentIntentRequest(
    Guid AppointmentId,
    Guid ClientId,
    decimal Amount);
