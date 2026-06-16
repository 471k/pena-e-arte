export {
  clientsApi,
  useGetMyClientQuery,
  useGetMyClientProfileQuery,
  useGetMyTattooRecordsQuery,
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
  useUpdatePortableProfileOptInMutation,
  useGetPortableProfileQuery,
} from "./clientsApi";
export type {
  ClientResponse,
  ClientProfileResponse,
  CreateClientRequest,
  UpsertClientProfileRequest,
  TattooRecordResponse,
  AddTattooRecordRequest,
  UpdateTattooRecordRequest,
  PortableTattooRecord,
  PortableClientProfile,
} from "./clientsApi";
export { ClientListPage } from "./components/ClientListPage";
export { CreateClientPage } from "./components/CreateClientPage";
export { ClientDetailPage } from "./components/ClientDetailPage";
export { MyProfilePage } from "./components/MyProfilePage";
export { TattooRecordDetailPage } from "./components/TattooRecordDetailPage";
export { TattooHistorySection } from "./components/TattooHistorySection";
export { PortableProfileToggle } from "./components/PortableProfileToggle";
export { BodyMap, ALL_BODY_ZONES } from "./components/BodyMap";
