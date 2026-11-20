namespace Pena_e_Arte.Contracts.Responses;

/// <summary>The studio's booking settings (C03 follow-up). SlotStepMinutes null = every session length.</summary>
public record BookingSettingsResponse(int? SlotStepMinutes, IReadOnlyList<int> AllowedStepMinutes);
