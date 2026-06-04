export {
  clientsApi,
  useGetClientsQuery,
  useGetClientByIdQuery,
  useCreateClientMutation,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
  useGetTattooRecordsQuery,
  useGetTattooRecordQuery,
  useAddTattooRecordMutation,
  useUpdateTattooRecordMutation,
  useDeleteTattooRecordMutation,
} from "./clientsApi";
export type {
  ClientResponse,
  ClientProfileResponse,
  CreateClientRequest,
  UpsertClientProfileRequest,
  TattooRecordResponse,
  AddTattooRecordRequest,
  UpdateTattooRecordRequest,
} from "./clientsApi";
export { ClientListPage } from "./components/ClientListPage";
export { CreateClientPage } from "./components/CreateClientPage";
export { ClientDetailPage } from "./components/ClientDetailPage";
export { TattooRecordDetailPage } from "./components/TattooRecordDetailPage";
export { BodyMap, ALL_BODY_ZONES } from "./components/BodyMap";
