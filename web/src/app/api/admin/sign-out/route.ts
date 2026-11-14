import { signOutAdmin } from "@/lib/auth/sign-in-server";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/admin/sign-out`: ends the admin's console session and goes to the sign-in page (W-1). A POST so a link
 * prefetch can never sign anyone out; same-origin only.
 *
 * @param request - The incoming form post.
 * @returns A redirect that clears the admin cookie.
 */
export async function POST(request: Request): Promise<Response> {
  return signOutAdmin(request, apiBaseUrl());
}
