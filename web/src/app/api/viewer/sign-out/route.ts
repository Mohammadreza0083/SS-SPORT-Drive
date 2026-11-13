import { signOut } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/viewer/sign-out`: ends the signed-in viewer's session and goes home (P-7, P-8). A POST so a link prefetch
 * can never sign anyone out; same-origin only.
 *
 * @param request - The incoming form post.
 * @returns A redirect to the home page that clears the account cookie.
 */
export async function POST(request: Request): Promise<Response> {
  return signOut(request, apiBaseUrl());
}
