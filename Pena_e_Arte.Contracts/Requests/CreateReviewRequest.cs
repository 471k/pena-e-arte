namespace Pena_e_Arte.Contracts.Requests;

// AppointmentId is required for studio/artist reviews (which completed visit this
// review is for) and unused for portfolio-image reviews.
public record CreateReviewRequest(int Rating, string Body, Guid? AppointmentId = null);
