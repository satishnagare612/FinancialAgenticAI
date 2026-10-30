export type ReportStatus =
  "pending" | "running" | "needs_review" | "completed" | "failed";

export interface ReportState {
  reportId: string;
  status: ReportStatus;
  correlationId: string;
  createdAtUtc: string;
  downloadJsonUrl?: string;
  downloadPdfUrl?: string;
}
export interface FinancialProfile {
  id: string;
  userId: string;
  initialBalance: number;
  monthlyContribution: number;
  expectedReturnRate: number;
  inflationRate: number;
  createdAtUtc: string;
}
export interface FinancialProfileInput {
  initialBalance: number;
  monthlyContribution: number;
  expectedReturnRate: number;
  inflationRate: number;
}
