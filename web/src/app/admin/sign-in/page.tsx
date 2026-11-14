import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Suspense } from "react";
import { createSignInClient } from "@/components/viewer/sign-in/sign-in-client";
import { signInCopy } from "@/components/viewer/sign-in/copy";
import { SignInPanel } from "@/components/viewer/sign-in/sign-in-panel";
import { ADMIN_COOKIE, readAdmin } from "@/lib/auth/sign-in-server";
import { apiBaseUrl } from "../../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: signInCopy.adminPageTitle,
  robots: { index: false, follow: false },
};

/**
 * `/admin/sign-in`: an admin asks for an email link, or finishes signing in from one (`?token=`), then opens the console
 * (W-1, A-1). An admin who is already signed in goes straight to `/admin`. Request data is read inside `<Suspense>`.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default function AdminSignInRoute({ searchParams }: PageProps<"/admin/sign-in">) {
  return (
    <Suspense fallback={null}>
      <AdminSignIn searchParams={searchParams} />
    </Suspense>
  );
}

/**
 * Reads the token, the error and the session on the server.
 *
 * @param props - The search params promise.
 * @param props.searchParams - The query string.
 * @returns The sign-in panel, or a redirect to the console.
 */
async function AdminSignIn({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
  const token = first(params.token);
  if (!token && (await readAdmin(apiBaseUrl(), (await cookies()).get(ADMIN_COOKIE)?.value))) {
    redirect("/admin");
  }

  return <SignInPanel audience="admin" token={token} error={first(params.error)} client={createSignInClient("admin")} />;
}
