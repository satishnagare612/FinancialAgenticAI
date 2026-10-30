import type {
  FinancialProfile,
  FinancialProfileInput,
  ReportState,
} from "./types";

const BASE =
  typeof window === "undefined"
    ? (process.env.INTERNAL_API_BASE_URL
        ?? process.env.NEXT_PUBLIC_API_BASE_URL
        ?? "http://localhost:8080")
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
let cachedToken: { value: string; expiresAt: number } | null = null;

async function getAuthToken(): Promise<string> {
  if (cachedToken && Date.now() < cachedToken.expiresAt - 60_000) {
    return cachedToken.value;
  }
  const res = await fetch(`${BASE}/api/auth/dev-token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId: "anonymous" }),
    signal: AbortSignal.timeout(5_000),
  });
  if (!res.ok) {
    throw new Error(`Auth failed: ${res.status} ${await res.text()}`);
  }
  const data = (await res.json()) as { token: string; userId: string };
  // Dev token lifetime is 1 hour. Cache for 55 minutes so the last five
  // minutes are always fresh.
  cachedToken = { value: data.token, expiresAt: Date.now() + 55 * 60 * 1000 };
  return data.token;
}

async function authedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const token = await getAuthToken();
  return fetch(`${BASE}${path}`, {
    ...init,
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
  input: FinancialProfileInput
): Promise<FinancialProfile> {
  const res = await authedFetch(`/api/profile`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
    signal: AbortSignal.timeout(15_000),
  });
  return handle<FinancialProfile>(res);
}
