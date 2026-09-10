import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  PackageResponse, CreatePackageRequest, UpdatePackageRequest,
  PackagePurchaseResponse, PurchasePackageRequest, PurchasePackageResponse,
} from "./packages.types";

export const packagesApi = createApi({
  reducerPath: "packagesApi",
  baseQuery,
  tagTypes: ["Package", "PackagePurchase"],
  endpoints: (builder) => ({
    getPackages: builder.query<PackageResponse[], void>({
      query: () => "packages",
      providesTags: ["Package"],
    }),
    createPackage: builder.mutation<PackageResponse, CreatePackageRequest>({
      query: (body) => ({ url: "packages", method: "POST", body }),
      invalidatesTags: ["Package"],
    }),
    updatePackage: builder.mutation<PackageResponse, { id: string; body: UpdatePackageRequest }>({
      query: ({ id, body }) => ({ url: `packages/${id}`, method: "PUT", body }),
      invalidatesTags: ["Package"],
    }),
    purchasePackage: builder.mutation<PurchasePackageResponse, PurchasePackageRequest>({
      query: (body) => ({ url: "packages/purchase", method: "POST", body }),
      invalidatesTags: ["PackagePurchase"],
    }),
    getMyPackagePurchases: builder.query<PackagePurchaseResponse[], void>({
      query: () => "packages/purchases/mine",
      providesTags: ["PackagePurchase"],
    }),
  }),
});

export const {
  useGetPackagesQuery,
  useCreatePackageMutation,
  useUpdatePackageMutation,
  usePurchasePackageMutation,
  useGetMyPackagePurchasesQuery,
} = packagesApi;
