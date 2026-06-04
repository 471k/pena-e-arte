export {
  clientsApi,
  useGetClientsQuery,
  useGetClientByIdQuery,
  useCreateClientMutation,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
} from "./clientsApi";
export type {
  ClientResponse,
  ClientProfileResponse,
  CreateClientRequest,
  UpsertClientProfileRequest,
} from "./clientsApi";
export { ClientListPage } from "./components/ClientListPage";
export { CreateClientPage } from "./components/CreateClientPage";
export { ClientDetailPage } from "./components/ClientDetailPage";
