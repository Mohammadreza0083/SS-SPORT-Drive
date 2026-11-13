import type { Metadata } from "next";
import { cookies, headers } from "next/headers";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { publicCopy } from "@/components/viewer/public/copy";
import { LockedPlayerPage } from "@/components/viewer/public/locked-page";
import { clientAddress } from "@/lib/viewer/gate-server";
import { ACCOUNT_COOKIE, loadLockedPage } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../../../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: publicCopy.lockedPageTitle,
};

/**
 * `/players/{slug}`: the locked player page (P-2, P-3, P-8). The player's public facts come from the API; videos and
 * documents are only titles, tags, lengths and page counts, so nothing on this page can play or open them. An unknown or
 * unpublished slug is a 404. The slug, cookie and proxy headers are request data, so they are read inside `<Suspense>`.
 *
 * @param props - The route params with the slug.
 * @returns The page.
 */
export default function PlayerRoute({ params }: PageProps<"/players/[slug]">) {
  return (
    <Suspense fallback={null}>
      <LockedPlayer params={params} />
    </Suspense>
  );
}

/**
 * Reads the locked page of a player on the server.
 *
 * @param props - The route params.
 * @param props.params - The params promise with the slug.
 * @returns The locked page.
 */
async function LockedPlayer({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const result = await loadLockedPage(apiBaseUrl(), slug, (await cookies()).get(ACCOUNT_COOKIE)?.value, fetch, clientAddress(await headers()));
  if (result.kind === "not-found") {
    notFound();
  }

  return <LockedPlayerPage data={result.data} />;
}
