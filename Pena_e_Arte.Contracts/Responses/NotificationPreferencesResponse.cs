namespace Pena_e_Arte.Contracts.Responses;

public record NotificationPreferenceItem(string Type, string Channel, bool IsEnabled);

public record NotificationPreferencesResponse(List<NotificationPreferenceItem> Preferences);
