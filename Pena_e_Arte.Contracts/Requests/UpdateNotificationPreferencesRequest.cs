using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Contracts.Requests;

public record UpdateNotificationPreferencesRequest(List<NotificationPreferenceItem> Preferences);
