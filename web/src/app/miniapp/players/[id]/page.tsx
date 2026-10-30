import type { Metadata } from "next";
import { Suspense } from "react";
import { miniAppCopy } from "../../copy";
import { PlayerDetailScreen } from "../my-players-screen";

export const metadata: Metadata = {
  title: "Player · SS-SPORT Drive",
  robots: { index: false, follow: false },
};

/**
 * `/miniapp/players/{id}`: one player's detail (T-7a). The id is request data, so it is passed on as a promise and
 * read inside `<Suspense>`; with `cacheComponents` the page shell prerenders and the detail streams in.
 *
 * @param props - The route params with the player id.
 * @returns The page.
 */
export default function PlayerDetailPage({ params }: PageProps<"/miniapp/players/[id]">) {
  return (
    <Suspense fallback={<p role="status">{miniAppCopy.signingIn}</p>}>
      <PlayerDetailScreen params={params} />
    </Suspense>
  );
}
