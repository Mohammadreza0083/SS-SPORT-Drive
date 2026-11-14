import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { apiBaseUrl } from "@/app/api/miniapp/api-base-url";
import { AdminApiError, adminFetch } from "./api";
import { ADMIN_COOKIE, readAdmin, type SignedInAdmin } from "./session";

/** Where an admin without a session goes. */
const SIGN_IN = "/admin/sign-in";

/**
 * Reads the admin's session token from the cookie, for server components.
 *
 * @returns The token, or undefined when nobody is signed in.
 */
export async function adminToken(): Promise<string | undefined> {
  return (await cookies()).get(ADMIN_COOKIE)?.value;
}

/**
 * Requires a signed-in admin: reads who it is, or sends the visitor to the sign-in page.
 *
 * @returns The admin and their token.
 */
export async function requireAdmin(): Promise<{ admin: SignedInAdmin; token: string }> {
  const token = await adminToken();
  const admin = await readAdmin(apiBaseUrl(), token);
  if (!token || !admin) {
    redirect(SIGN_IN);
  }

  return { admin, token };
}

/**
 * Reads an admin endpoint of the API from a server component. A refused session goes to the sign-in page; an unknown
 * thing is a 404 page; anything else is thrown so the page shows its error.
 *
 * @typeParam T - The answer's shape.
 * @param token - The admin session token.
 * @param path - The API path with its query, starting with `/admin/`.
 * @returns The parsed answer.
 */
export async function adminGet<T>(token: string, path: string): Promise<T> {
  try {
    return await adminFetch<T>(apiBaseUrl(), token, path);
  } catch (error) {
    if (error instanceof AdminApiError && error.status === 401) {
      redirect(SIGN_IN);
    }

    if (error instanceof AdminApiError && error.status === 404) {
      notFound();
    }

    throw error;
  }
}

/**
 * Reads a query parameter's first value.
 *
 * @param params - Next.js `searchParams`.
 * @param name - The parameter name.
 * @returns The value, or undefined.
 */
export function firstParam(params: Record<string, string | string[] | undefined>, name: string): string | undefined {
  const value = params[name];
  return Array.isArray(value) ? value[0] : value;
}

/**
 * Reads a page number from the address.
 *
 * @param params - Next.js `searchParams`.
 * @returns The page, at least 1.
 */
export function pageParam(params: Record<string, string | string[] | undefined>): number {
  const value = Number(firstParam(params, "page"));
  return Number.isInteger(value) && value >= 1 ? value : 1;
}
