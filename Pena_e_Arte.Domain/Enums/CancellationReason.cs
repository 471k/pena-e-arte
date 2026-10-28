namespace Pena_e_Arte.Domain.Enums;

public enum CancellationReason
{
    ClientNoShow,
    StudioCancelled,
    ClientCancelled,
    // The studio turned down a booking request (still Pending) rather than cancelling a booking
    // it had accepted; the client gets the "can't take your request" wording instead.
    StudioDeclined
}
