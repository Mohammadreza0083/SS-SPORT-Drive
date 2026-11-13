import type { Metadata } from "next";
import { cookies, headers } from "next/headers";
import { Suspense } from "react";
import { RemoteHomePage } from "@/components/viewer/public/home-page";
import { publicCopy } from "@/components/viewer/public/copy";
import { clientAddress } from "@/lib/viewer/gate-server";
import { ACCOUNT_COOKIE, loadHome } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: publicCopy.pageTitle,
  description: publicCopy.lead,
};

/**
 * `/`: the public home page (P-1, P-7, P-8). The API searches published players by name and position, one page at a
 * time; `?q=`, `?position=` and `?page=` carry the state, so the player QR code's `/?q=<name>` opens the search.
 * Search params, cookies and proxy headers are request data, so they are read inside `<Suspense>`.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default function HomeRoute({ searchParams }: PageProps<"/">) {
  return (
    <Suspense fallback={null}>
      <Home searchParams={searchParams} />
    </Suspense>
  );
}

/**
 * Reads the page of the search and who is signed in on the server.
 *
 * @param props - The search params promise.
 * @param props.searchParams - The query string.
 * @returns The home page.
 */
async function Home({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
  const data = await loadHome(
    apiBaseUrl(),
    { q: first(params.q), position: first(params.position), page: first(params.page) },
    (await cookies()).get(ACCOUNT_COOKIE)?.value,
    fetch,
    clientAddress(await headers()),
  );
  return <RemoteHomePage data={data} />;
}
