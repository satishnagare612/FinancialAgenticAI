import { getProfile } from "@/lib/api";
import { ProfileForm } from "@/components/profile-form";

export default async function ProfilePage() {
    let profile = null;
    try {
        profile = await getProfile();
    } catch {
        // Network error — form defaults will still let the user save.
    }
    return (
        <main className="container mx-auto p-6">
            <ProfileForm initial={profile} />
        </main>
    );
}