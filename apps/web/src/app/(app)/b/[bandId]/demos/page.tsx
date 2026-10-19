import { redirect } from "next/navigation";

// Demos folded into Songs (oct 2026). Old links keep working.
export default async function DemosRedirect({ params }: { params: Promise<{ bandId: string }> }) {
  const { bandId } = await params;
  redirect(`/b/${bandId}/songs`);
}
