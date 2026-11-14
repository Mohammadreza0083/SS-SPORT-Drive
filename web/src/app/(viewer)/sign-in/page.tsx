import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Suspense } from "react";
import { createSignInClient } from "@/components/viewer/sign-in/sign-in-client";
import { signInCopy } from "@/components/viewer/sign-in/copy";
import { SignInPanel } from "@/components/viewer/sign-in/sign-in-panel";
import { ACCOUNT_COOKIE } from "@/lib/viewer/public-server";

export const metadata: Metadata = {
  title: signInCopy.pageTitle,
  robots: { index: false, follow: false },
};

/**
 * `/sign-in`: a viewer asks for an email link, or finishes signing in from one (`?token=`), then returns to the search
 * (W-1, P-5, P-7). A visitor who already has a session cookie goes straight home. Request data is read inside
 * `<Suspense>`.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default function ViewerSignInRoute({ searchParams }: PageProps<"/sign-in">) {
  return (
    <Suspense fallback={null}>
      <ViewerSignIn searchParams={searchParams} />
    </Suspense>
  );
}

/**
 * Reads the token, the error and the cookie on the server.
 *
 * @param props - The search params promise.
 * @param props.searchParams - The query string.
 * @returns The sign-in panel, or a redirect home.
 */
async function ViewerSignIn({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
  const token = first(params.token);
  if (!token && (await cookies()).get(ACCOUNT_COOKIE)?.value) {
    redirect("/");
  }

  return <SignInPanel audience="viewer" token={token} error={first(params.error)} client={createSignInClient("viewer")} />;
}
