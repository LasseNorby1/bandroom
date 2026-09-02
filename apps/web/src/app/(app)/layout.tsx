import { Wordmark } from "@/components/wordmark";
import { Providers } from "./providers";
import { SignOutButton } from "./sign-out-button";
import Link from "next/link";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <Providers>
      <div className="mx-auto flex min-h-dvh w-full max-w-3xl flex-col px-5">
        <header className="flex items-center justify-between py-5">
          <Link href="/home" aria-label="Home">
            <Wordmark />
          </Link>
          <SignOutButton />
        </header>
        <main className="flex-1 pb-16">{children}</main>
      </div>
    </Providers>
  );
}
