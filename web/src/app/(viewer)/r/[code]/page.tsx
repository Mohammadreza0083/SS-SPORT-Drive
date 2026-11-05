import type { Metadata } from "next";
import { cookies } from "next/headers";
import { Suspense } from "react";
import { EmailGate } from "@/components/viewer/gate/email-gate";
import { gateCopy } from "@/components/viewer/gate/copy";
import { LinkUnavailable } from "@/components/viewer/gate/link-unavailable";
import { SESSION_COOKIE } from "@/lib/viewer/gate";
import { readSession, resolveLink } from "@/lib/viewer/gate-server";
import { apiBaseUrl } from "../../../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: gateCopy.pageTitle,
  robots: { index: false, follow: false },
};

/**
 * `/r/{code}`: a personal link (L-2). A link that does not open shows "not available"; otherwise the email gate, or,
 * with a session for this link, the gate's "You're in" step until the player page arrives with V-2b. The code and the
 * session cookie are request data, so they are read inside `<Suspense>`.
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
 * Resolves the link and the viewer's session on the server.
 *
 * @param props - The route params with the link code.
 * @param props.params - The params promise.
 * @returns The gate, the signed-in step or the "not available" page.
 */
async function PersonalLink({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const api = apiBaseUrl();
  const link = await resolveLink(api, code);
  if (!link) {
    return <LinkUnavailable />;
  }

  const session = await readSession(api, code, (await cookies()).get(SESSION_COOKIE)?.value);
  return <EmailGate link={link} signedIn={session !== null} />;
}
