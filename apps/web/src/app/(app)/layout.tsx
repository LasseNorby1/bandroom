import { InstallBanner } from "@/components/install-banner";
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
          <nav className="flex items-center gap-1">
            <Link href="/account" className="px-3 py-1.5 text-sm text-muted hover:text-ink">
              Account
            </Link>
            <SignOutButton />
          </nav>
        </header>
        <main className="flex-1 pb-16">
          <InstallBanner />
          {children}
        </main>
      </div>
    </Providers>
  );
}
