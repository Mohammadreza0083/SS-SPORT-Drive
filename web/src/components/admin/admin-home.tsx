import type { SignedInAdmin } from "@/lib/auth/sign-in-server";
import { signInCopy } from "../viewer/sign-in/copy";

/**
 * `/admin` after sign-in (W-1). The console itself is Epic 7; until then the page says who is signed in and offers Sign
 * out, a POST form so a link prefetch can never sign anyone out.
 *
 * @param props - The props.
 * @param props.admin - Who is signed in.
 * @returns The placeholder page.
 */
export function AdminHome({ admin }: { admin: SignedInAdmin }) {
  return (
    <main className="gate">
      <div className="gate-column">
        <div className="gate-body">
          <div className="gate-step">
            <h1 className="gate-title">{signInCopy.adminHomeTitle}</h1>
            <p className="gate-intro">{signInCopy.signedInAs(admin.displayName, admin.email)}</p>
            <form method="post" action={signInCopy.adminSignOutAction}>
              <button type="submit" className="gate-button">
                {signInCopy.adminSignOut}
              </button>
            </form>
          </div>
        </div>
      </div>
    </main>
  );
}
