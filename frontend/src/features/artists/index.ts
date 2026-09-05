export {
  artistsApi,
  useCreateArtistMutation,
  useCreateOwnArtistProfileMutation,
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} from "./artistsApi";
export type { ArtistResponse, CreateArtistRequest, CreateOwnArtistProfileRequest, UpdateArtistRequest } from "./artistsApi";
export { ArtistListPage } from "./components/ArtistListPage";
export { ArtistDetailPage } from "./components/ArtistDetailPage";
export { CreateArtistPage } from "./components/CreateArtistPage";
