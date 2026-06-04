export {
  artistsApi,
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} from "./artistsApi";
export type { ArtistResponse, UpdateArtistRequest } from "./artistsApi";
export { ArtistListPage } from "./components/ArtistListPage";
export { ArtistDetailPage } from "./components/ArtistDetailPage";
