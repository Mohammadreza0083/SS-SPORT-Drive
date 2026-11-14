import { adminSignOut } from "@/lib/admin/session";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/admin/sign-out`: ends the admin session and clears the cookie. A POST so a link prefetch can never sign anyone out.
 *
 * @param request - The Sign out button's form post.
 * @returns A redirect to the sign-in page.
 */
export async function POST(request: Request): Promise<Response> {
  return adminSignOut(request, apiBaseUrl());
}
