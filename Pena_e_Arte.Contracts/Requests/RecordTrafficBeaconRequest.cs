namespace Pena_e_Arte.Contracts.Requests;

// Deliberately minimal — no client-supplied studioId/role/userId (those are derived
// server-side from the JWT and from resolving Path against known public-page slugs,
// never trusted from the client, to prevent a caller from spoofing another studio's/
// role's traffic numbers).
public record RecordTrafficBeaconRequest(string Path, bool IsNavigation);
