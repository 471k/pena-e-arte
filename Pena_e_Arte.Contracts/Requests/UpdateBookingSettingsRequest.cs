namespace Pena_e_Arte.Contracts.Requests;

/// <summary>SlotStepMinutes null = every session length; otherwise one of BookingSettingsResponse.AllowedStepMinutes.</summary>
public record UpdateBookingSettingsRequest(int? SlotStepMinutes);
