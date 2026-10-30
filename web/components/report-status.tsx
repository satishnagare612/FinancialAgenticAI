"use client";
import { useEffect, useState } from "react";
import { getReport } from "@/lib/api";
import type { ReportState } from "@/lib/types";

export function ReportStatus({ initial }: { initial: ReportState }) {
  const [state, setState] = useState(initial);

  useEffect(() => {
    if (state.status === "completed" || state.status === "failed") return;
    const interval = setInterval(async () => {
      try {
        setState(await getReport(state.reportId));
      } catch (err) {
        console.error("Failed to fetch status update", err);
      }
    }, 3000);
    return () => clearInterval(interval);
  }, [state.reportId, state.status]);

  return (
    <div className="p-4 border rounded shadow-sm bg-white">
      <div className="text-lg font-semibold">Status: {state.status}</div>
      <div className="text-sm text-gray-500">Report ID: {state.reportId}</div>
      {state.downloadPdfUrl && (
        <a
          href={state.downloadPdfUrl}
          className="mt-2 inline-block text-blue-600 underline"
        >
          Download PDF
        </a>
      )}
      {state.downloadJsonUrl && (
        <a
          href={state.downloadJsonUrl}
          className="mt-2 ml-3 inline-block text-blue-600 underline"
        >
          Download JSON
        </a>
      )}
    </div>
  );
}
