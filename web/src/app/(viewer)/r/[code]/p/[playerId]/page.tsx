import type { Metadata } from "next";
import { cookies, headers } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { Suspense } from "react";
import { gateCopy } from "@/components/viewer/gate/copy";
import { PlayerPage } from "@/components/viewer/player-page/player-page";
import { clientAddress, loadPlayerPage } from "@/lib/viewer/content-server";
import { SESSION_COOKIE } from "@/lib/viewer/gate";
import { apiBaseUrl } from "../../../../../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: gateCopy.pageTitle,
  robots: { index: false, follow: false },
};

/**
 * `/r/{code}/p/{playerId}`: one player of a Roster link (V-2b), opened from a roster card. Without a live session the
 * viewer goes back to `/r/{code}` and its gate; a player who is not on the link is 404.
 *
 * @param props - The route params with the link code and the player id.
 * @returns The page.
 */
export default function LinkPlayerPage({ params }: PageProps<"/r/[code]/p/[playerId]">) {
  return (
    <Suspense fallback={null}>
      <LinkPlayer params={params} />
    </Suspense>
  );
}

/**
 * Loads the player on the server.
 *
 * @param props - The route params.
 * @param props.params - The params promise.
 * @returns The player page.
 */
async function LinkPlayer({ params }: { params: Promise<{ code: string; playerId: string }> }) {
  const { code, playerId } = await params;
  const page = await loadPlayerPage(apiBaseUrl(), code, playerId, (await cookies()).get(SESSION_COOKIE)?.value, clientAddress(await headers()));
  if (page.kind === "not-found") {
    notFound();
  }

  if (page.kind === "gate") {
    redirect(`/r/${encodeURIComponent(code)}`);
  }

  return <PlayerPage data={page.data} />;
}
