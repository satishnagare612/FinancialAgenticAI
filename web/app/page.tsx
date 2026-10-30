import Link from "next/link";
import {
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";

export default function HomePage() {
    return (
        <main className="container mx-auto p-6 space-y-8 max-w-5xl">
            <header className="space-y-2">
                <h1 className="text-3xl font-bold tracking-tight">FinancialAI</h1>
                <p className="text-muted-foreground">
                    Deterministic financial projections with AI-assisted explanations.
                    Calculations are performed server-side in .NET; AI generates only the
                    narrative, risks, and citations.
                </p>
            </header>

            <div className="grid gap-4 md:grid-cols-2">
                <Card>
                    <CardHeader>
                        <CardTitle>Financial Profile</CardTitle>
                        <CardDescription>
                            Set your starting balance, monthly contribution, expected return,
                            and inflation assumptions. Profiles are required before creating
                            a report.
                        </CardDescription>
                    </CardHeader>
                    <CardContent>
                        <Button asChild>
                            <Link href="/profile">Edit profile</Link>
                        </Button>
                    </CardContent>
                </Card>

                <Card>
                    <CardHeader>
                        <CardTitle>Reports</CardTitle>
                        <CardDescription>
                            Create a projection report. Download the structured JSON or a
                            rendered PDF once review is complete.
                        </CardDescription>
                    </CardHeader>
                    <CardContent>
                        <Button asChild>
                            <Link href="/reports">View reports</Link>
                        </Button>
                    </CardContent>
                </Card>
            </div>

            <footer className="text-xs text-muted-foreground">
                Reports are not financial, tax, or legal advice. Assumptions are
                disclosed with every projection.
            </footer>
        </main>
    );
}