import type {
  FinancialProfile,
  FinancialProfileInput,
  ReportState,
} from "./types";

const BASE =
  typeof window === "undefined"
    ? (process.env.INTERNAL_API_BASE_URL ??
      process.env.NEXT_PUBLIC_API_BASE_URL ??
      "http://localhost:8080")
    : (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080");

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`API ${res.status}: ${body}`);
  }
  return res.json() as Promise<T>;
}

// ---------- DEV-ONLY AUTH ----------
// Dev token caching. In production this is replaced by a real session.
// /api/auth/dev-token returns 404 outside Development, so this code path
// naturally fails there without a build flag.
// Cached token is BROWSER-ONLY. On the server (Vercel Server Components /
// Route Handlers), module state persists across requests on a warm
// instance, so a cached token can outlive a signing-key rotation and
// produce 401s until the cache ages out. The dev-token endpoint is a
// stub — fetching per-request on the server is cheap and correct.
let browserCachedToken: { value: string; expiresAt: number } | null = null;

async function getAuthToken(): Promise<string> {
  const isBrowser = typeof window !== "undefined";

  if (
    isBrowser &&
    browserCachedToken &&
    Date.now() < browserCachedToken.expiresAt - 60_000
  ) {
    return browserCachedToken.value;
  }

  const res = await fetch(`${BASE}/api/auth/dev-token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId: "anonymous" }),
    signal: AbortSignal.timeout(5_000),
    // Never cache token fetches. Without this, Next.js 14 may cache the
    // response in its Data Cache and return a stale token for hours,
    // producing 401s on every server render long after key rotation.
    cache: "no-store",
  });
  if (!res.ok) {
    throw new Error(`Auth failed: ${res.status} ${await res.text()}`);
  }
  const data = (await res.json()) as { token: string; userId: string };

  if (isBrowser) {
    // 55-minute cache so the last 5 minutes are always fresh.
    browserCachedToken = {
      value: data.token,
      expiresAt: Date.now() + 55 * 60 * 1000,
    };
  }

  return data.token;
}

async function authedFetch(
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const token = await getAuthToken();
  return fetch(`${BASE}${path}`, {
    ...init,
    // API responses are per-token, per-user, and time-sensitive — the
    // Next.js Data Cache must not serve them. Every call must go to the
    // API. `init.cache` (if provided by a caller) overrides.
    cache: init.cache ?? "no-store",
    headers: {
      ...(init.headers as Record<string, string> | undefined),
      Authorization: `Bearer ${token}`,
    },
  });
}
// ---------- END DEV-ONLY AUTH ----------

export async function createReport(input: {
  reportType: string;
  projectionYears: number;
  currency: string;
  includeScenarioAnalysis: boolean;
}): Promise<ReportState> {
  const res = await authedFetch(`/api/reports`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
    signal: AbortSignal.timeout(15_000),
  });
  return handle<ReportState>(res);
}

export async function getReport(id: string): Promise<ReportState> {
  const res = await authedFetch(`/api/reports/${id}`, {
    signal: AbortSignal.timeout(10_000),
  });
  return handle<ReportState>(res);
}

export async function getReportDetail(id: string): Promise<unknown> {
  const res = await authedFetch(`/api/reports/${id}/detail`, {
    signal: AbortSignal.timeout(10_000),
  });
  return handle<unknown>(res);
}

export async function getProfile(): Promise<FinancialProfile | null> {
  const res = await authedFetch(`/api/profile`, {
    signal: AbortSignal.timeout(10_000),
  });
  if (res.status === 404) return null;
  return handle<FinancialProfile>(res);
}

export async function upsertProfile(
  input: FinancialProfileInput,
): Promise<FinancialProfile> {
  const res = await authedFetch(`/api/profile`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
    signal: AbortSignal.timeout(15_000),
  });
  return handle<FinancialProfile>(res);
}
