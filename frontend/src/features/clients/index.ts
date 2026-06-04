export {
  clientsApi,
  useGetClientsQuery,
  useGetClientByIdQuery,
  useCreateClientMutation,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
  useGetTattooRecordsQuery,
  useAddTattooRecordMutation,
} from "./clientsApi";
export type {
  ClientResponse,
  ClientProfileResponse,
  CreateClientRequest,
  UpsertClientProfileRequest,
  TattooRecordResponse,
  AddTattooRecordRequest,
} from "./clientsApi";
export { ClientListPage } from "./components/ClientListPage";
export { CreateClientPage } from "./components/CreateClientPage";
export { ClientDetailPage } from "./components/ClientDetailPage";
export { BodyMap, ALL_BODY_ZONES } from "./components/BodyMap";
