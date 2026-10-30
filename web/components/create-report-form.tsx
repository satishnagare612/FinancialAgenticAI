"use client";
import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { createReportAction } from "@/app/actions/create-report";

export function CreateReportForm() {
    const [pending, startTransition] = useTransition();
    const [error, setError] = useState<string | null>(null);
    const router = useRouter();

    function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
        e.preventDefault();
        setError(null);
        const data = new FormData(e.currentTarget);

        startTransition(async () => {
            try {
                const report = await createReportAction({
                    reportType: String(data.get("reportType")),
                    projectionYears: Number(data.get("projectionYears")),
                    currency: String(data.get("currency")),
                    includeScenarioAnalysis: data.get("includeScenarioAnalysis") === "on",
                });
                router.push(`/report/${report.reportId}`);
            } catch (err) {
                const msg = err instanceof Error ? err.message : "Unknown error";

                // The API returns 409 with "profile_required" when the user has
                // no profile yet. Bounce to /profile so they can create one.
                if (msg.includes("profile_required")) {
                    router.push("/profile");
                    return;
                }
                setError(msg);
            }
        });
    }

    return (
        <Card className="max-w-xl">
            <CardHeader>
                <CardTitle>Create a new report</CardTitle>
            </CardHeader>
            <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <Label htmlFor="reportType">Report type</Label>
                        <Input
                            id="reportType"
                            name="reportType"
                            defaultValue="retirement-projection"
                            required
                        />
                    </div>
                    <div>
                        <Label htmlFor="projectionYears">Projection years</Label>
                        <Input
                            id="projectionYears"
                            name="projectionYears"
                            type="number"
                            min="1"
                            max="100"
                            defaultValue={20}
                            required
                        />
                    </div>
                    <div>
                        <Label htmlFor="currency">Currency</Label>
                        <Input id="currency" name="currency" defaultValue="USD" required />
                    </div>
                    <div className="flex items-center gap-2">
                        <input
                            id="includeScenarioAnalysis"
                            name="includeScenarioAnalysis"
                            type="checkbox"
                            defaultChecked
                        />
                        <Label htmlFor="includeScenarioAnalysis">Include scenario analysis</Label>
                    </div>

                    {error && (
                        <Alert variant="destructive">
                            <AlertDescription>{error}</AlertDescription>
                        </Alert>
                    )}

                    <Button type="submit" disabled={pending}>
                        {pending ? "Creating…" : "Create report"}
                    </Button>
                </form>
            </CardContent>
        </Card>
    );
}