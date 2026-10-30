import { notFound } from "next/navigation";
import Link from "next/link";
import { getReport, getReportDetail } from "@/lib/api";
import { ReportStatus } from "@/components/report-status";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import type { ReportState } from "@/lib/types";

interface PageProps {
  params: { id: string };
}

interface ReportDetail {
  narrative: string | null;
  keyRisks: string[];
  assumptions: string[];
  citedSources: string[];
  confidenceScore: number;
}

export default async function ReportDetailPage({ params }: PageProps) {
  let state: ReportState;
  try {
    state = await getReport(params.id);
  } catch (err) {
    if (err instanceof Error && err.message.includes("404")) {
      notFound();
    }
    return (
      <main className="container mx-auto p-6 max-w-3xl">
        <Card>
          <CardHeader>
            <CardTitle>Could not load report</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-destructive">
              {err instanceof Error ? err.message : "Unknown error"}
            </p>
            <Button asChild className="mt-4">
              <Link href="/reports">Back to reports</Link>
            </Button>
          </CardContent>
        </Card>
      </main>
    );
  }

  // Detail fetched through the authenticated client helper.
  // getReportDetail uses authedFetch, which attaches the Bearer token.
  // A raw fetch would 401 because /api/reports/{id}/detail requires auth.
  let detail: ReportDetail | null = null;
  try {
    detail = (await getReportDetail(state.reportId)) as ReportDetail;
  } catch {
    // Swallow — the status panel still renders.
  }

  return (
    <main className="container mx-auto p-6 space-y-6 max-w-3xl">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Report</h1>
        <Button asChild variant="outline">
          <Link href="/reports">New report</Link>
        </Button>
      </div>

      <ReportStatus initial={state} />

      {state.status === "completed" && detail && (
        <>
          {detail.narrative && (
            <Card>
              <CardHeader>
                <CardTitle>Narrative</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="whitespace-pre-wrap text-sm leading-relaxed">
                  {detail.narrative}
                </p>
              </CardContent>
            </Card>
          )}

          {detail.keyRisks.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Key risks</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="list-disc pl-6 space-y-1 text-sm">
                  {detail.keyRisks.map((risk, i) => (
                    <li key={i}>{risk}</li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          {detail.assumptions.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Assumptions</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="list-disc pl-6 space-y-1 text-sm">
                  {detail.assumptions.map((a, i) => (
                    <li key={i}>{a}</li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          {detail.citedSources.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Citations</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="list-disc pl-6 space-y-1 text-sm">
                  {detail.citedSources.map((src, i) => (
                    <li key={i}>{src}</li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>Confidence</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm">
                {(detail.confidenceScore * 100).toFixed(1)}%
              </p>
            </CardContent>
          </Card>
        </>
      )}

      {state.status === "needs_review" && (
        <Card>
          <CardHeader>
            <CardTitle>Awaiting human review</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground">
              This report was flagged for review because confidence fell below
              the configured threshold or the revision cap was reached. It will
              not be marked final until a reviewer records a decision.
            </p>
          </CardContent>
        </Card>
      )}

      {state.status === "failed" && (
        <Card>
          <CardHeader>
            <CardTitle>Report failed</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-destructive">
              The workflow encountered an error. Check server logs with the
              correlation ID or create a new report.
            </p>
          </CardContent>
        </Card>
      )}
    </main>
  );
}
