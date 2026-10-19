import { redirect } from "next/navigation";

// An idea kept its id when it became a song, so the old url still lands.
export default async function IdeaRedirect({ params }: { params: Promise<{ bandId: string; ideaId: string }> }) {
  const { bandId, ideaId } = await params;
  redirect(`/b/${bandId}/songs/${ideaId}`);
}
