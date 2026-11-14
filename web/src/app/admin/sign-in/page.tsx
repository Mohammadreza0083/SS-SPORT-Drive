import type { Metadata } from "next";
import { Suspense } from "react";
import { adminCopy } from "@/components/admin/copy";
import { SignInForm } from "@/components/admin/shell/sign-in-form";
import { BrandLogo } from "@/components/viewer/brand-logo";
import { firstParam } from "@/lib/admin/server";

export const metadata: Metadata = { title: adminCopy.signIn.title };

/**
 * `/admin/sign-in`: the admin's email-link sign-in (W-1 admin half, canvas `AdminLogin`). Without a token it asks for
 * the email; with `?token=` (from the emailed link) it only shows a Continue button that posts the token, so a mail
 * scanner that opens the link cannot use it up. `?error=link` says the link did not work.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default function AdminSignInPage({ searchParams }: PageProps<"/admin/sign-in">) {
  return (
    <Suspense fallback={null}>
      <SignIn searchParams={searchParams} />
    </Suspense>
  );
}

/**
 * Reads the search params and picks the step.
 *
 * @param props - The props.
 * @param props.searchParams - The query string.
 * @returns The card.
 */
async function SignIn({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const params = await searchParams;
  const token = firstParam(params, "token");
  const failed = firstParam(params, "error") === "link";
  return (
    <div className="adm-login">
      <div className="adm-login-card">
        <BrandLogo height={34} alt={adminCopy.brand} priority />
        {token ? (
          <form method="post" action="/api/admin/session">
            <h1>{adminCopy.signIn.linkTitle}</h1>
            <p className="adm-lead">{adminCopy.signIn.linkLead}</p>
            <input type="hidden" name="token" value={token} />
            <button type="submit" className="adm-btn adm-btn-red adm-btn-wide">
              {adminCopy.signIn.continue}
            </button>
          </form>
        ) : (
          <>
            {failed ? (
              <p role="alert" className="adm-error">
                {adminCopy.signIn.expired}
              </p>
            ) : null}
            <SignInForm />
          </>
        )}
      </div>
    </div>
  );
}
