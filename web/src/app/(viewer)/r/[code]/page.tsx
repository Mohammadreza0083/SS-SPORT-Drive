import type { Metadata } from "next";
import { cookies, headers } from "next/headers";
import { Suspense } from "react";
import { EmailGate } from "@/components/viewer/gate/email-gate";
import { gateCopy } from "@/components/viewer/gate/copy";
import { LinkUnavailable } from "@/components/viewer/gate/link-unavailable";
import { PlayerPage } from "@/components/viewer/player-page/player-page";
import { RosterPage } from "@/components/viewer/roster/roster-page";
import { clientAddress, loadViewerPage } from "@/lib/viewer/content-server";
import { SESSION_COOKIE } from "@/lib/viewer/gate";
import { resolveLink } from "@/lib/viewer/gate-server";
import { apiBaseUrl } from "../../../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: gateCopy.pageTitle,
  robots: { index: false, follow: false },
};

/**
 * `/r/{code}`: a personal link. A link that does not open shows "not available"; without a session for it, the email
 * gate (L-2); with one, a Personal link's player page or a Roster link's roster (V-2b). The code, the cookie and the
 * proxy headers are request data, so they are read inside `<Suspense>`.
 *
 * @param props - The route params with the link code.
 * @returns The page.
 */
export default function PersonalLinkPage({ params }: PageProps<"/r/[code]">) {
  return (
    <Suspense fallback={null}>
      <PersonalLink params={params} />
    </Suspense>
  );
}

/**
 * Resolves the link and what the viewer may see on the server.
 *
 * @param props - The route params with the link code.
 * @param props.params - The params promise.
 * @returns The gate, the player page, the roster or the "not available" page.
 */
async function PersonalLink({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const api = apiBaseUrl();
  const clientIp = clientAddress(await headers());
  const link = await resolveLink(api, code, fetch, clientIp);
  if (!link) {
    return <LinkUnavailable />;
  }

  const page = await loadViewerPage(api, code, (await cookies()).get(SESSION_COOKIE)?.value, clientIp);
  switch (page.kind) {
    case "player":
      return <PlayerPage data={page.data} />;
    case "roster":
      return <RosterPage data={page.data} year={new Date().getFullYear()} />;
    default:
      return <EmailGate link={link} />;
  }
}
