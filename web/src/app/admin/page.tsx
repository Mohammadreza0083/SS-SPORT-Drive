import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Suspense } from "react";
import { AdminHome } from "@/components/admin/admin-home";
import { signInCopy } from "@/components/viewer/sign-in/copy";
import { ADMIN_COOKIE, readAdmin } from "@/lib/auth/sign-in-server";
import { apiBaseUrl } from "../api/miniapp/api-base-url";

export const metadata: Metadata = {
  title: signInCopy.adminHomeTitle,
  robots: { index: false, follow: false },
};

/**
 * `/admin`: the console's landing page (W-1). Nobody signed in goes to the admin sign-in page. The console itself is
 * Epic 7. The cookie is request data, so it is read inside `<Suspense>`.
 *
 * @returns The page.
 */
export default function AdminRoute() {
  return (
    <Suspense fallback={null}>
      <Admin />
    </Suspense>
  );
}

/**
 * Reads who is signed in on the server.
 *
 * @returns The console landing page, or a redirect to the sign-in page.
 */
async function Admin() {
  const admin = await readAdmin(apiBaseUrl(), (await cookies()).get(ADMIN_COOKIE)?.value);
  if (!admin) {
    redirect("/admin/sign-in");
  }

  return <AdminHome admin={admin} />;
}
