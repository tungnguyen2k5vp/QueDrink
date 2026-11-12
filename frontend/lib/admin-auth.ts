export type AdminSession = { accessToken: string; refreshToken: string; expiresAt: number; displayName: string; email: string | null };

const KEY = "quedrink-admin-session";

export function readAdminSession(): AdminSession | null {
  if (typeof window === "undefined") return null;
  try {
    const session = JSON.parse(sessionStorage.getItem(KEY) || "null") as AdminSession | null;
    if (!session || !session.accessToken || !session.refreshToken || !session.expiresAt) {
      sessionStorage.removeItem(KEY);
      return null;
    }
    return session;
  } catch {
    sessionStorage.removeItem(KEY);
    return null;
  }
}

export function writeAdminSession(session: AdminSession) { sessionStorage.setItem(KEY, JSON.stringify(session)); }
export function clearAdminSession() { if (typeof window !== "undefined") sessionStorage.removeItem(KEY); }
