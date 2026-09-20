import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams, useSearchParams, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import {
  ArrowLeft,
  Banknote,
  Calendar,
  ChevronRight,
  ExternalLink,
  ImagePlus,
  Loader2,
  Mail,
  MoreHorizontal,
  Pencil,
  Send,
  Tag,
  Trash2,
  X,
} from "lucide-react";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import { Badge } from "@/shared/components/ui/badge";
import { SubscriptionGatedButton } from "@/shared/components/SubscriptionGatedButton";
import { SpecializationsField } from "@/shared/components/SpecializationsField";
import { TATTOO_STYLE_OPTIONS } from "@/shared/constants/tattooStyles";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/shared/components/ui/tabs";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useAppSelector } from "@/app/hooks";
import {
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useUpdateArtistPortfolioMutation,
  useDeleteArtistMutation,
  useResendArtistInviteMutation,
} from "../artistsApi";
import { usePresignedUpload } from "@/shared/hooks/usePresignedUpload";
import { useGetDesignsQuery, useMarkDesignAsCatalogItemMutation } from "@/features/designs/designsApi";
import type { DesignResponse } from "@/features/designs/design.types";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { ArtistScheduleEditor } from "./ArtistScheduleEditor";
import { ArtistSocialTab } from "./ArtistSocialTab";

// Keep in sync with TattooStyle.cs constants on the backend.
const STYLE_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "blackwork",       label: "Blackwork"       },
  { value: "realism",         label: "Realism"         },
  { value: "traditional",     label: "Traditional"     },
  { value: "geometric",       label: "Geometric"       },
  { value: "watercolor",      label: "Watercolor"      },
  { value: "fineline",        label: "Fineline"        },
  { value: "neo-traditional", label: "Neo-Traditional" },
  { value: "japanese",        label: "Japanese"        },
];

// Keep in sync with PortfolioImageCategory.cs constants on the backend.
const CATEGORY_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "fresh",  label: "Fresh Tattoo"  },
  { value: "healed", label: "Healed Tattoo" },
  { value: "design", label: "Design"        },
];

const editSchema = z.object({
  firstName:  z.string().min(1, "First name is required"),
  lastName:   z.string().min(1, "Last name is required"),
  email:      z.string().email("Invalid email"),
  hourlyRate: z.number({ message: "Must be a number" }).positive("Must be positive").max(10_000).optional(),
  slug:       z.string().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/, "Lowercase letters, numbers, hyphens only").optional().or(z.literal("")),
});

type EditFormValues = z.infer<typeof editSchema>;

function specializationLabel(style: string): string {
  return TATTOO_STYLE_OPTIONS.find((o) => o.value === style)?.label ?? style;
}

function getInitials(firstName: string, lastName: string): string {
  return `${firstName[0] ?? ""}${lastName[0] ?? ""}`.toUpperCase();
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

const VALID_TABS = ["profile", "portfolio", "hours", "bookings", "designs", "social"] as const;
type ArtistDetailTab = (typeof VALID_TABS)[number];

function isValidTab(value: string | null): value is ArtistDetailTab {
  return value !== null && (VALID_TABS as readonly string[]).includes(value);
}

function DesignCatalogControls({ design }: { design: DesignResponse }) {
  const [markAsCatalogItem, { isLoading }] = useMarkDesignAsCatalogItemMutation();
  const [priceInput, setPriceInput] = useState(design.price != null ? String(design.price) : "");

  async function handleToggle(next: boolean) {
    const price = next ? (parseFloat(priceInput) || null) : null;
    const result = await markAsCatalogItem({ id: design.id, body: { isCatalogItem: next, price } });
    if ("data" in result) {
      toast.success(next ? "Marked as flash catalog item." : "Removed from flash catalog.");
    } else {
      toast.error("Failed to update — this design may already be tied to an approved client revision.");
    }
  }

  async function handlePriceBlur() {
    if (!design.isCatalogItem) return;
    const price = parseFloat(priceInput) || null;
    if (price === design.price) return;
    const result = await markAsCatalogItem({ id: design.id, body: { isCatalogItem: true, price } });
    if ("data" in result) toast.success("Flash price updated.");
  }

  return (
    <div
      className="flex items-center gap-3 px-3 pb-3 pt-1 border-t"
      onClick={(e) => e.stopPropagation()}
    >
      <label className="flex items-center gap-1.5 text-xs cursor-pointer select-none">
        <input
          type="checkbox"
          checked={design.isCatalogItem}
          disabled={isLoading}
          onChange={(e) => void handleToggle(e.target.checked)}
          className="h-3.5 w-3.5 rounded border-input accent-primary"
        />
        <Tag className="h-3 w-3" />
        Flash catalog item
      </label>
      {design.isCatalogItem && (
        <Input
          type="number"
          min="0.01"
          step="0.01"
          placeholder="Price (€)"
          value={priceInput}
          onChange={(e) => setPriceInput(e.target.value)}
          onBlur={() => void handlePriceBlur()}
          disabled={isLoading}
          className="h-7 w-24 text-xs"
        />
      )}
    </div>
  );
}

export function ArtistDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const canManage = usePermission(Role.Owner);
  const isArtistRole = usePermission(Role.Artist);
  const currentUserId = useAppSelector((s) => s.auth.user?.id);

  const { data: artist, isLoading, isError } = useGetArtistByIdQuery(id!);

  const isOwnProfile = isArtistRole && artist?.userId != null && artist.userId === currentUserId;

  useDocumentMeta({
    title: artist
      ? isOwnProfile
        ? `${artist.firstName} ${artist.lastName} — TattooOS`
        : `${artist.firstName} ${artist.lastName} — Artists — TattooOS`
      : "Artists — TattooOS",
    canonical: `/artists/${id ?? ""}`,
  });

  const [updateArtist,    { isLoading: isSaving }]   = useUpdateArtistMutation();
  const [updatePortfolio, { isLoading: isSavingPf }] = useUpdateArtistPortfolioMutation();
  const [deleteArtist,    { isLoading: isDeleting }] = useDeleteArtistMutation();
  const [resendInvite,    { isLoading: isResending }] = useResendArtistInviteMutation();

  const { upload, isUploading } = usePresignedUpload();

  const [isEditing,  setIsEditing]  = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [specializations, setSpecializations] = useState<string[]>([]);

  const { data: designs = [], isLoading: designsLoading } =
    useGetDesignsQuery({ artistId: id! }, { skip: !id });

  const { data: appointments = [], isLoading: appsLoading } =
    useGetAppointmentsQuery(canManage && id ? { artistId: id } : {}, { skip: !id });

  const rawTab = searchParams.get("tab");
  const activeTab: ArtistDetailTab = isValidTab(rawTab) ? rawTab : "profile";
  const tabListRef = useRef<HTMLDivElement>(null);

  function handleTabChange(next: string): void {
    const params = new URLSearchParams(searchParams);
    params.set("tab", next);
    setSearchParams(params, { replace: true });
  }

  // OAuth-redirect landing: toast the outcome once, switch to the Social tab, and strip the
  // one-shot params so a refresh doesn't re-toast. Deliberately runs only for the params
  // present on first mount, not when the user later navigates tabs normally.
  useEffect(() => {
    const ig = searchParams.get("instagram");
    const social = searchParams.get("social");
    const platform = searchParams.get("platform");

    if (ig === null && social === null) return;

    if (ig === "connected") toast.success("Instagram connected successfully!");
    if (ig === "error")     toast.error("Instagram connection failed. Please try again.");
    if (ig === "denied")    toast.info("Instagram connection cancelled.");
    if (social === "connected") toast.success(`${platform ?? "Account"} connected successfully!`);
    if (social === "error")     toast.error(`${platform ?? "Account"} connection failed. Please try again.`);
    if (social === "denied")    toast.info(`${platform ?? "Account"} connection cancelled.`);

    const params = new URLSearchParams(searchParams);
    params.set("tab", "social");
    params.delete("instagram");
    params.delete("social");
    params.delete("platform");
    setSearchParams(params, { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep the active tab in view when the strip scrolls horizontally on narrow screens.
  useEffect(() => {
    const active = tabListRef.current?.querySelector<HTMLElement>('[role="tab"][data-state="active"]');
    active?.scrollIntoView?.({ inline: "center", block: "nearest", behavior: "smooth" });
  }, [activeTab]);

  const canManagePortfolio = canManage || isOwnProfile;

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<EditFormValues>({
    resolver: zodResolver(editSchema),
  });

  function startEdit() {
    if (!artist) return;
    reset({
      firstName:  artist.firstName,
      lastName:   artist.lastName,
      email:      artist.email,
      hourlyRate: artist.hourlyRate ?? undefined,
      slug:       artist.slug ?? "",
    });
    setSpecializations(artist.specializations);
    setIsEditing(true);
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    const result = await updateArtist({
      id,
      body: {
        firstName:       values.firstName,
        lastName:        values.lastName,
        email:           values.email,
        specializations: specializations.length > 0 ? specializations : null,
        hourlyRate:      values.hourlyRate ?? null,
        slug:            values.slug?.trim() || undefined,
      },
    });
    if ("data" in result) {
      toast.success("Artist updated.");
      setIsEditing(false);
    } else {
      toast.error("Failed to update artist.");
    }
  }

  async function onDelete() {
    if (!id) return;
    const result = await deleteArtist(id);
    if ("error" in result) {
      toast.error("Failed to delete artist.");
      return;
    }
    navigate("/artists");
  }

  async function onResendInvite() {
    if (!id) return;
    const result = await resendInvite(id);
    if ("error" in result) {
      toast.error("Failed to resend invite email.");
    } else {
      toast.success("Invite email resent.");
    }
  }

  function openImagePicker(category: string) {
    if (!id || !artist) return;
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/*";
    input.onchange = async () => {
      const file = input.files?.[0];
      input.remove();
      if (!file) return;
      const objectKey = `portfolio/${id}/${Date.now()}-${file.name.replace(/\s+/g, "_")}`;
      const publicUrl = await upload(file, objectKey);
      if (!publicUrl) {
        toast.error("Image upload failed.");
        return;
      }
      const images = [
        ...artist.portfolioImages.map((p) => ({ imageUrl: p.imageUrl, style: p.style, category: p.category })),
        { imageUrl: publicUrl, style: null, category },
      ];
      const result = await updatePortfolio({ id, images });
      if ("error" in result) {
        toast.error("Failed to save portfolio image.");
      } else {
        const label = CATEGORY_OPTIONS.find((c) => c.value === category)?.label ?? category;
        toast.success(`${label} added to portfolio. Pick a style so it shows up under Discover filters.`);
      }
    };
    document.body.appendChild(input);
    input.click();
  }

  async function removePortfolioImage(imageId: string) {
    if (!id || !artist) return;
    const images = artist.portfolioImages
      .filter((p) => p.imageId !== imageId)
      .map((p) => ({ imageUrl: p.imageUrl, style: p.style, category: p.category }));
    const result = await updatePortfolio({ id, images });
    if ("error" in result) {
      toast.error("Failed to remove image.");
    }
  }

  async function updateImageStyle(imageId: string, style: string | null) {
    if (!id || !artist) return;
    const images = artist.portfolioImages.map((p) => ({
      imageUrl: p.imageUrl,
      style:    p.imageId === imageId ? style : p.style,
      category: p.category,
    }));
    const result = await updatePortfolio({ id, images });
    if ("error" in result) {
      toast.error("Failed to update style.");
    }
  }

  async function updateImageCategory(imageId: string, category: string | null) {
    if (!id || !artist) return;
    const images = artist.portfolioImages.map((p) => ({
      imageUrl: p.imageUrl,
      style:    p.style,
      category: p.imageId === imageId ? category : p.category,
    }));
    const result = await updatePortfolio({ id, images });
    if ("error" in result) {
      toast.error("Failed to update category.");
    }
  }

  if (isLoading) {
    return (
      <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
        <Skeleton className="h-14 w-14 rounded-full" />
        <Skeleton className="h-6 w-48" />
        <Skeleton className="h-24 w-full" />
      </main>
    );
  }

  if (isError || !artist) {
    return (
      <main className="flex flex-col items-center justify-center gap-4 px-4 py-16">
        <p className="text-sm text-destructive-text">Artist not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/artists")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Artists
        </Button>
      </main>
    );
  }

  return (
    <>
      <main className="max-w-2xl mx-auto px-4 py-8 space-y-6">
        {!isOwnProfile && (
          <nav aria-label="Breadcrumb" className="text-sm text-muted-foreground">
            <Link to="/artists" className="hover:text-foreground transition-colors">Artists</Link>
            <span className="mx-1.5" aria-hidden="true">/</span>
            <span className="text-foreground">{artist.firstName} {artist.lastName}</span>
          </nav>
        )}

        <div className="flex items-center justify-between gap-4">
          <div className="flex items-center gap-4 min-w-0">
            <Avatar className="h-14 w-14 text-base shrink-0">
              <AvatarFallback className="bg-primary text-primary-foreground font-semibold">
                {getInitials(artist.firstName, artist.lastName)}
              </AvatarFallback>
            </Avatar>
            <div className="min-w-0">
              <h1 className="text-lg font-semibold leading-tight truncate">
                {artist.firstName} {artist.lastName}
              </h1>
              <p className="text-sm text-muted-foreground">Artist</p>
            </div>
          </div>

          {isEditing ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setIsEditing(false)}
              disabled={isSaving}
            >
              Cancel
            </Button>
          ) : (canManage || isOwnProfile) && (
            <div className="flex items-center gap-2 shrink-0">
              <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
                <Pencil className="h-3.5 w-3.5" />
                Edit
              </Button>
              {canManage && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline" size="icon" className="h-8 w-8" aria-label="More actions">
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem
                      onSelect={() => setDeleteOpen(true)}
                      className="text-destructive-text focus:text-destructive-text"
                    >
                      <Trash2 className="h-3.5 w-3.5 mr-2" />
                      {isOwnProfile ? "Stop working as an artist" : "Delete"}
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
            </div>
          )}
        </div>

        {isEditing ? (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Artist</h2>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="firstName">First name</Label>
                <Input
                  id="firstName"
                  {...register("firstName")}
                  className={cn(errors.firstName && "border-destructive")}
                />
                {errors.firstName && (
                  <p className="text-xs text-destructive-text">{errors.firstName.message}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="lastName">Last name</Label>
                <Input
                  id="lastName"
                  {...register("lastName")}
                  className={cn(errors.lastName && "border-destructive")}
                />
                {errors.lastName && (
                  <p className="text-xs text-destructive-text">{errors.lastName.message}</p>
                )}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                {...register("email")}
                className={cn(errors.email && "border-destructive")}
              />
              {errors.email && (
                <p className="text-xs text-destructive-text">{errors.email.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="specializations">Specializations (optional)</Label>
              <SpecializationsField id="specializations" value={specializations} onChange={setSpecializations} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="hourlyRate">Hourly rate (€, optional)</Label>
              <Input
                id="hourlyRate"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g. 90"
                {...register("hourlyRate", { setValueAs: (v) => (v === "" || v == null ? undefined : Number(v)) })}
              />
              <p className="text-xs text-muted-foreground">
                Used to calculate percentage-based booking deposits.
              </p>
              {errors.hourlyRate && (
                <p className="text-xs text-destructive-text">{errors.hourlyRate.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="slug">Public profile slug (optional)</Label>
              <Input
                id="slug"
                placeholder="e.g. elena-martins"
                {...register("slug")}
              />
              <p className="text-xs text-muted-foreground">
                Used in the public portfolio URL: /artist/your-slug
              </p>
              {errors.slug && (
                <p className="text-xs text-destructive-text">{errors.slug.message}</p>
              )}
            </div>

            <SubscriptionGatedButton type="submit" className="w-full" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Changes"
              )}
            </SubscriptionGatedButton>
          </form>
        ) : (
          <Tabs value={activeTab} onValueChange={handleTabChange}>
            <TabsList
              ref={tabListRef}
              className="h-auto w-full justify-start sm:justify-center overflow-x-auto sm:overflow-x-visible flex sm:grid sm:grid-cols-6 gap-1 sm:gap-0 scroll-smooth"
            >
              <TabsTrigger value="profile"   className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Profile</TabsTrigger>
              <TabsTrigger value="portfolio" className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Portfolio</TabsTrigger>
              <TabsTrigger value="hours"     className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Availability</TabsTrigger>
              <TabsTrigger value="bookings"  className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Bookings</TabsTrigger>
              <TabsTrigger value="designs"   className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Designs</TabsTrigger>
              <TabsTrigger value="social"    className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Social</TabsTrigger>
            </TabsList>

            {/* Profile tab */}
            <TabsContent value="profile" className="mt-4 space-y-4">
              <Card>
                <CardContent className="p-4 space-y-3">
                  <div className="flex items-center justify-between gap-2 text-sm">
                    <div className="flex items-center gap-2 min-w-0">
                      <Mail className="h-4 w-4 shrink-0 text-muted-foreground" />
                      <span className="truncate">{artist.email}</span>
                    </div>
                    {canManage && !isOwnProfile && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="gap-1.5 shrink-0 text-xs h-7"
                        disabled={isResending}
                        onClick={() => void onResendInvite()}
                      >
                        {isResending ? (
                          <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                          <Send className="h-3.5 w-3.5" />
                        )}
                        Resend invite
                      </Button>
                    )}
                  </div>

                  {artist.specializations.length > 0 && (
                    <div className="flex items-start gap-2 text-sm">
                      <Tag className="h-4 w-4 shrink-0 mt-0.5 text-muted-foreground" />
                      <div className="flex flex-wrap gap-1">
                        {artist.specializations.map((style) => (
                          <Badge key={style} variant="secondary">{specializationLabel(style)}</Badge>
                        ))}
                      </div>
                    </div>
                  )}

                  {artist.hourlyRate != null && (
                    <div className="flex items-center gap-2 text-sm">
                      <Banknote className="h-4 w-4 shrink-0 text-muted-foreground" />
                      <span>€{artist.hourlyRate.toFixed(2)} / hour</span>
                    </div>
                  )}

                  <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
                    <Calendar className="h-3.5 w-3.5 shrink-0" />
                    <span>Joined {formatDate(artist.createdAt)}</span>
                  </div>

                  {isOwnProfile && artist.slug && (
                    <a
                      href={`${import.meta.env.VITE_PUBLIC_URL ?? window.location.origin}/artist/${artist.slug}`}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
                    >
                      View public profile
                      <ExternalLink className="h-3 w-3" />
                    </a>
                  )}
                </CardContent>
              </Card>

              {canManage && (
                <p className="text-xs text-muted-foreground text-center">
                  Last updated {formatDate(artist.updatedAt)}
                </p>
              )}
            </TabsContent>

            {/* Portfolio tab */}
            <TabsContent value="portfolio" className="mt-4">
              {canManagePortfolio && (
                <div className="flex justify-end mb-3">
                  <DropdownMenu modal={false}>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="outline"
                        size="sm"
                        className="gap-1.5"
                        disabled={isUploading || isSavingPf}
                      >
                        {isUploading || isSavingPf ? (
                          <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                          <ImagePlus className="h-3.5 w-3.5" />
                        )}
                        Add image
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      {CATEGORY_OPTIONS.map(({ value, label }) => (
                        <DropdownMenuItem
                          key={value}
                          onSelect={() => {
                            // Deferred (not prevented — a prevented onSelect keeps the dropdown
                            // open indefinitely): opening the native file picker synchronously
                            // from a DropdownMenuItem select races the menu's own close/focus-
                            // return behavior, the same class of overlay-interaction issue
                            // documented elsewhere in this codebase for Dialog-based overlays.
                            setTimeout(() => openImagePicker(value), 0);
                          }}
                        >
                          {label}
                        </DropdownMenuItem>
                      ))}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )}

              {artist.portfolioImages.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-12 gap-2 text-center">
                  <p className="text-sm font-medium">No portfolio images yet</p>
                  {canManagePortfolio && (
                    <p className="text-xs text-muted-foreground">
                      Upload fresh tattoos, healed results, or designs, and tag each with a style so
                      they appear on the public discover feed and its filters.
                    </p>
                  )}
                </div>
              ) : (
                <div className="columns-2 md:columns-3 gap-3 space-y-3">
                  {artist.portfolioImages.map(({ imageId, imageUrl, style, category }) => (
                    <div key={imageId} className="relative break-inside-avoid group space-y-1.5">
                      <div className="relative">
                        <img
                          src={imageUrl}
                          alt="Portfolio image"
                          className="w-full rounded-lg object-cover"
                          onError={(e) => {
                            const img = e.currentTarget;
                            img.style.display = "none";
                            const placeholder = img.nextElementSibling as HTMLElement | null;
                            if (placeholder) placeholder.style.display = "flex";
                          }}
                        />
                        <div
                          style={{ display: "none" }}
                          className="w-full h-32 rounded-lg bg-muted/60 border border-border/40
                                     flex-col items-center justify-center gap-1 text-center px-2"
                        >
                          <p className="text-xs text-muted-foreground">Image unavailable</p>
                          <p className="text-[10px] text-muted-foreground break-all line-clamp-2">{imageUrl}</p>
                        </div>
                        {canManagePortfolio && (
                          <button
                            type="button"
                            aria-label="Remove image"
                            onClick={() => void removePortfolioImage(imageId)}
                            className="absolute top-1.5 right-1.5 rounded-full bg-black/60 p-0.5 opacity-0 group-hover:opacity-100 transition-opacity focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                          >
                            <X className="h-3.5 w-3.5 text-white" />
                          </button>
                        )}
                      </div>
                      {canManagePortfolio ? (
                        <>
                          <Select
                            value={category ?? "none"}
                            onValueChange={(v) => void updateImageCategory(imageId, v === "none" ? null : v)}
                          >
                            <SelectTrigger
                              aria-label="Portfolio category"
                              className={cn("h-7 text-xs", !category && "text-muted-foreground")}
                            >
                              <SelectValue placeholder="Uncategorized" />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem value="none">Uncategorized</SelectItem>
                              {CATEGORY_OPTIONS.map(({ value, label }) => (
                                <SelectItem key={value} value={value}>{label}</SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                          <Select
                            value={style ?? "none"}
                            onValueChange={(v) => void updateImageStyle(imageId, v === "none" ? null : v)}
                          >
                            <SelectTrigger
                              aria-label="Tattoo style"
                              className={cn("h-7 text-xs", !style && "text-muted-foreground")}
                            >
                              <SelectValue placeholder="No style" />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem value="none">No style</SelectItem>
                              {STYLE_OPTIONS.map(({ value, label }) => (
                                <SelectItem key={value} value={value}>{label}</SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </>
                      ) : (
                        <div className="space-y-0.5 px-1">
                          {category && (
                            <Badge variant="secondary" className="text-[10px]">
                              {CATEGORY_OPTIONS.find((c) => c.value === category)?.label ?? category}
                            </Badge>
                          )}
                          {style && (
                            <p className="text-xs text-muted-foreground">
                              {STYLE_OPTIONS.find((s) => s.value === style)?.label ?? style}
                            </p>
                          )}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </TabsContent>

            {/* Working hours / time off tab */}
            <TabsContent value="hours" className="mt-4">
              <ArtistScheduleEditor artistId={id!} canEdit={canManage || isOwnProfile} />
            </TabsContent>

            {/* Bookings tab */}
            <TabsContent value="bookings" className="mt-4 space-y-3">
              {appsLoading && (
                <div className="space-y-2">
                  {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 w-full" />)}
                </div>
              )}
              {!appsLoading && appointments.length === 0 && (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No appointments found.
                </p>
              )}
              {!appsLoading && appointments.length > 0 && (
                <div className="space-y-2">
                  {appointments.map((appt) => (
                    <Link
                      key={appt.id}
                      to={`/appointments/${appt.id}`}
                      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                    >
                      <Card className="hover:bg-muted/40 transition-colors">
                        <CardContent className="p-3 flex items-center justify-between gap-3">
                          <div className="space-y-1 min-w-0">
                            <p className="text-sm font-medium">
                              {formatDate(appt.date)}
                            </p>
                            <p className="text-xs text-muted-foreground">
                              {appt.durationMinutes} min
                            </p>
                          </div>
                          <div className="flex items-center gap-2 shrink-0">
                            <AppointmentStatusBadge status={appt.status} />
                            <ChevronRight className="h-4 w-4 text-muted-foreground" />
                          </div>
                        </CardContent>
                      </Card>
                    </Link>
                  ))}
                </div>
              )}
            </TabsContent>

            {/* Designs tab */}
            <TabsContent value="designs" className="mt-4 space-y-3">
              {designsLoading && (
                <div className="space-y-2">
                  {[1, 2, 3].map((i) => <Skeleton key={i} className="h-14 w-full" />)}
                </div>
              )}
              {!designsLoading && designs.length === 0 && (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No designs found.
                </p>
              )}
              {!designsLoading && designs.length > 0 && (
                <div className="space-y-2">
                  {designs.map((design) => (
                    <Card key={design.id} className="hover:bg-muted/40 transition-colors">
                      <Link
                        to={`/designs/${design.id}`}
                        className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-t-lg"
                      >
                        <CardContent className="p-3 flex items-center justify-between gap-3">
                          <div className="min-w-0">
                            <p className="text-sm font-medium truncate">{design.title}</p>
                            {design.description && (
                              <p className="text-xs text-muted-foreground line-clamp-1">
                                {design.description}
                              </p>
                            )}
                          </div>
                          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                        </CardContent>
                      </Link>
                      {canManagePortfolio && (
                        <DesignCatalogControls design={design} />
                      )}
                    </Card>
                  ))}
                </div>
              )}
            </TabsContent>

            {/* Social tab — one list of connection rows; Instagram's photo sync sits under its row. */}
            <TabsContent value="social" className="mt-6">
              <ArtistSocialTab
                artistId={artist.id}
                firstName={artist.firstName}
                slug={artist.slug ?? null}
                canManage={canManage}
                isOwnProfile={isOwnProfile}
              />
            </TabsContent>
          </Tabs>
        )}
      </main>

      {/* Delete confirmation dialog */}
      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {isOwnProfile ? "Stop working as an artist?" : `Delete ${artist.firstName} ${artist.lastName}?`}
            </DialogTitle>
            <DialogDescription>
              {isOwnProfile
                ? "This removes your artist profile — your owner login and studio access are unaffected. This action cannot be undone."
                : "This action cannot be undone."}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setDeleteOpen(false)}
              disabled={isDeleting}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={onDelete}
              disabled={isDeleting}
            >
              {isDeleting ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  {isOwnProfile ? "Removing…" : "Deleting…"}
                </>
              ) : (
                isOwnProfile ? "Stop working as an artist" : "Delete"
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
