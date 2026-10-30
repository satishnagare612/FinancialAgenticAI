"use server";
import { upsertProfile } from "@/lib/api";
import type { FinancialProfileInput } from "@/lib/types";

export type UpsertProfileResult = { ok: true } | { ok: false; error: string };

export async function upsertProfileAction(
  input: FinancialProfileInput,
): Promise<UpsertProfileResult> {
  try {
    await upsertProfile(input);
    return { ok: true };
  } catch (err) {
    return {
      ok: false,
      error: err instanceof Error ? err.message : "Unknown error",
    };
  }
}
