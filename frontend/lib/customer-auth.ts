export type CustomerSession = { accessToken: string; refreshToken: string; expiresAt: number; user: { id: string; displayName: string; email: string | null; phone: string | null; roles: string[] } };
const KEY = "quedrink-customer-session";
export function readCustomerSession(): CustomerSession | null {
  if (typeof window === "undefined") return null;
  try {
    const session = JSON.parse(sessionStorage.getItem(KEY) || "null") as CustomerSession | null;
    return session?.accessToken && session.refreshToken && session.user?.id ? session : null;
  } catch { return null; }
}
export function writeCustomerSession(session: CustomerSession) { sessionStorage.setItem(KEY, JSON.stringify(session)); window.dispatchEvent(new Event("quedrink-customer")); }
export function clearCustomerSession() { if (typeof window !== "undefined") { sessionStorage.removeItem(KEY); window.dispatchEvent(new Event("quedrink-customer")); } }
