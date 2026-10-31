import { NextRequest, NextResponse } from "next/server";
import { fetchReportPdfRaw } from "@/lib/api";

export async function GET(
  _req: NextRequest,
  { params }: { params: { id: string } },
) {
  try {
    const res = await fetchReportPdfRaw(params.id);
    if (!res.ok) {
      return NextResponse.json(
        { error: `API ${res.status}: ${await res.text()}` },
        { status: res.status },
      );
    }
    const buf = await res.arrayBuffer();
    return new NextResponse(buf, {
      status: 200,
      headers: {
        "Content-Type": "application/pdf",
        "Content-Disposition": `attachment; filename="report-${params.id}.pdf"`,
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
