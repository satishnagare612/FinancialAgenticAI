import { redirect } from "next/navigation";
import { getProfile } from "@/lib/api";
import { CreateReportForm } from "@/components/create-report-form";

export default async function ReportsPage() {
  let profile = null;
  try {
    profile = await getProfile();
  } catch {
    // Network or API error — let the user see the page; the form's submit
    // will surface the real error if they try to create a report.
  }

  if (!profile) {
    redirect("/profile");
  }

  return (
    <main className="container mx-auto p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Reports</h1>
      <CreateReportForm />
    </main>
  );
}
