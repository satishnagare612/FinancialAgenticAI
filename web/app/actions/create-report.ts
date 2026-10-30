"use server";
import { createReport } from "@/lib/api";

export async function createReportAction(input: {
  reportType: string;
  projectionYears: number;
  currency: string;
  includeScenarioAnalysis: boolean;
}) {
  return createReport(input);
}
