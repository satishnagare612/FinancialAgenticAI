import { NextRequest, NextResponse } from "next/server";
import { fetchReportJsonRaw } from "@/lib/api";

export async function GET(
  _req: NextRequest,
  { params }: { params: { id: string } },
) {
  try {
    const res = await fetchReportJsonRaw(params.id);
    if (!res.ok) {
      return NextResponse.json(
        { error: `API ${res.status}: ${await res.text()}` },
        { status: res.status },
      );
    }
    const body = await res.text();
    return new NextResponse(body, {
      status: 200,
      headers: {
        "Content-Type": "application/json; charset=utf-8",
        "Content-Disposition": `attachment; filename="report-${params.id}.json"`,
        "Cache-Control": "no-store",
      },
    });
  } catch (err) {
    return NextResponse.json(
      { error: err instanceof Error ? err.message : "Unknown error" },
      { status: 502 },
    );
  }
}
