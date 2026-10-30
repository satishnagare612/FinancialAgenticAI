"use client";
import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { upsertProfileAction } from "@/app/actions/upsert-profile";
import type { FinancialProfile } from "@/lib/types";

export function ProfileForm({ initial }: { initial: FinancialProfile | null }) {
    const [pending, startTransition] = useTransition();
    const [error, setError] = useState<string | null>(null);
    const router = useRouter();

    function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
        e.preventDefault();
        setError(null);
        const data = new FormData(e.currentTarget);

        startTransition(async () => {
            const result = await upsertProfileAction({
                initialBalance: Number(data.get("initialBalance")),
                monthlyContribution: Number(data.get("monthlyContribution")),
                expectedReturnRate: Number(data.get("expectedReturnRate")),
                inflationRate: Number(data.get("inflationRate")),
            });
            if (!result.ok) {
                setError(result.error);
                return;
            }
            router.push("/reports");
        });
    }

    return (
        <Card className="max-w-xl">
            <CardHeader>
                <CardTitle>Financial Profile</CardTitle>
            </CardHeader>
            <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <Label htmlFor="initialBalance">Initial balance</Label>
                        <Input id="initialBalance" name="initialBalance" type="number" step="0.01" min="0"
                            defaultValue={initial?.initialBalance ?? 100000} required />
                    </div>
                    <div>
                        <Label htmlFor="monthlyContribution">Monthly contribution</Label>
                        <Input id="monthlyContribution" name="monthlyContribution" type="number"
                            step="0.01" min="0" defaultValue={initial?.monthlyContribution ?? 500} required />
                    </div>
                    <div>
                        <Label htmlFor="expectedReturnRate">Expected annual return (e.g. 0.06)</Label>
                        <Input id="expectedReturnRate" name="expectedReturnRate" type="number"
                            step="0.001" min="-0.99" max="1" defaultValue={initial?.expectedReturnRate ?? 0.06} required />
                    </div>
                    <div>
                        <Label htmlFor="inflationRate">Annual inflation (e.g. 0.025)</Label>
                        <Input id="inflationRate" name="inflationRate" type="number"
                            step="0.001" min="-0.99" max="1" defaultValue={initial?.inflationRate ?? 0.025} required />
                    </div>

                    {error && (
                        <Alert variant="destructive">
                            <AlertDescription>{error}</AlertDescription>
                        </Alert>
                    )}

                    <Button type="submit" disabled={pending}>
                        {pending ? "Saving…" : "Save profile"}
                    </Button>
                </form>
            </CardContent>
        </Card>
    );
}