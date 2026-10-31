import { createContext } from "react";
import type { MoneyStudio } from "@/shared/hooks/useEurAllRate";

/**
 * The signed-in staff member's own studio (country + currency), provided once by OwnerLayout and
 * ArtistLayout so every <Money> on a staff screen knows whether to show lek next to euro (Albanian EUR
 * studios, spec Q3 decided 2026-10-26: staff screens show it too) without each page threading it
 * through. Null outside those layouts: <Money> then shows the plain amount unless given a `studio` prop.
 */
export const MoneyStudioContext = createContext<MoneyStudio | null>(null);
