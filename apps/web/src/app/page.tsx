import Link from "next/link";
import { Wordmark } from "@/components/wordmark";
import { cn } from "@/lib/cn";

// The front door for people who aren't signed in (the proxy sends members to
// /home). Static, no client js: the product's own tokens, the pitch from the
// spec, and one call to action — registration is open while in early access.

const DAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;
type Cell = "free" | "block" | "off";
const MEMBERS: { name: string; week: Cell[] }[] = [
  { name: "Lasse", week: ["free", "block", "free", "free", "free", "free", "off"] },
  { name: "Mikkel", week: ["off", "free", "free", "free", "block", "free", "off"] },
  { name: "Sofie", week: ["block", "free", "off", "free", "free", "free", "off"] },
  { name: "Jonas", week: ["free", "free", "free", "free", "free", "block", "off"] },
];
const COUNTS = DAYS.map((_, day) => MEMBERS.filter((m) => m.week[day] === "free").length);

const WAVE = [30, 55, 42, 70, 48, 62, 35, 58, 78, 52, 44, 66, 38, 72, 56, 47, 63, 41, 75, 50, 68, 45, 59, 36, 64, 49, 71, 43, 57, 33, 61, 39, 66, 52, 46, 58];

const buttonPrimary =
  "inline-flex h-11 items-center justify-center rounded-full bg-ink px-6 text-sm font-medium text-paper transition-opacity hover:opacity-90";
const buttonOutline =
  "inline-flex h-11 items-center justify-center rounded-full border border-line px-6 text-sm font-medium text-ink transition-colors hover:border-ink";

function Eyebrow({ children }: { children: React.ReactNode }) {
  return (
    <p className="flex items-center gap-3 font-mono text-xs tracking-wider text-accent">
      <span>{children}</span>
      <span className="h-px flex-1 bg-line" aria-hidden />
    </p>
  );
}

function WeekGrid() {
  return (
    <figure className="min-w-0 space-y-3 rounded-xl border border-line bg-surface p-4 sm:p-5">
      <div className="overflow-x-auto">
        <table className="border-separate border-spacing-1" aria-label="Four members' availability over one week; Thursday is the only day all four are free">
          <thead>
            <tr>
              <th aria-hidden className="w-16" />
              {DAYS.map((day) => (
                <th key={day} className={cn("pb-1 font-mono text-[11px] font-normal text-muted", day === "thu" && "text-ink")}>
                  {day}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {MEMBERS.map((member) => (
              <tr key={member.name}>
                <td className="pr-2 text-sm">{member.name}</td>
                {member.week.map((state, i) => (
                  <td key={DAYS[i]}>
                    <div
                      className={cn(
                        "flex size-8 items-center justify-center rounded-md font-mono text-[10px] text-faint sm:size-9",
                        state === "free" && "bg-ok-soft",
                        state === "block" && "border border-line bg-paper",
                        state === "off" && "border border-dashed border-line",
                      )}
                    >
                      {state === "block" && "✕"}
                    </div>
                  </td>
                ))}
              </tr>
            ))}
            <tr>
              <td className="pr-2 font-mono text-[11px] text-muted">everyone?</td>
              {COUNTS.map((free, i) => (
                <td key={DAYS[i]} className="text-center">
                  <span
                    className={cn(
                      "inline-block rounded-full px-1.5 py-0.5 font-mono text-[10px]",
                      free === MEMBERS.length ? "bg-ok text-white" : "text-muted",
                    )}
                  >
                    {free}/{MEMBERS.length}
                  </span>
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>
      <figcaption className="flex flex-wrap items-center gap-x-4 gap-y-1 font-mono text-[11px] text-muted">
        <span className="text-accent">→ finder proposes: thursday 19–22</span>
        <span className="flex items-center gap-1.5"><span className="size-3 rounded bg-ok-soft" /> free</span>
        <span className="flex items-center gap-1.5"><span className="flex size-3 items-center justify-center rounded border border-line text-[8px]">✕</span> blockout</span>
        <span className="flex items-center gap-1.5"><span className="size-3 rounded border border-dashed border-line" /> outside pattern</span>
      </figcaption>
    </figure>
  );
}

function PlayerMock() {
  const played = 20;
  return (
    <div className="space-y-3 rounded-xl border border-line bg-surface p-4 sm:p-5" role="img" aria-label="A demo player: Silverline, bridge idea v3, with a comment pinned at 1:23">
      <div className="flex items-baseline gap-2">
        <span className="font-mono text-[11px] text-accent">v3</span>
        <span className="min-w-0 flex-1 truncate text-sm font-medium">Silverline — bridge idea</span>
        <span className="font-mono text-[11px] text-faint">lasse · 02:41</span>
      </div>
      <div className="flex items-center gap-3">
        <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-ink text-paper">▶</span>
        <div className="relative flex h-11 flex-1 items-end gap-px">
          {WAVE.map((h, i) => (
            <span key={i} style={{ height: `${h}%` }} className={cn("min-w-0 flex-1 rounded-sm", i < played ? "bg-ink" : "bg-line")} />
          ))}
          <span className="absolute -top-1.5 left-[62%] size-2.5 rounded-full border-2 border-surface bg-accent" aria-hidden />
        </div>
        <span className="w-10 shrink-0 text-right font-mono text-[11px] text-muted">1:23</span>
      </div>
      <p className="flex items-baseline gap-2 text-sm">
        <span className="font-mono text-[11px] text-accent">1:23</span>
        <span className="font-medium">Sofie</span>
        <span className="text-muted">chorus should hit harder here — double the toms?</span>
      </p>
    </div>
  );
}

export default function Landing() {
  return (
    <div className="mx-auto w-full max-w-5xl px-5">
      <header className="flex items-center justify-between py-5">
        <Wordmark />
        <nav className="flex items-center gap-4 text-sm">
          <Link href="/login" className="text-muted hover:text-ink">Sign in</Link>
          <Link href="/register" className={cn(buttonPrimary, "h-9 px-4")}>Start your room</Link>
        </nav>
      </header>

      <main className="space-y-24 pb-24 pt-10 sm:pt-16">
        <section className="max-w-3xl space-y-6">
          <p className="inline-flex items-center gap-2 rounded-full border border-line px-3 py-1 font-mono text-[11px] tracking-wider text-muted">
            <span className="size-1.5 rounded-full bg-accent" aria-hidden />
            early access · free for your band
          </p>
          <h1 className="font-display text-4xl font-bold leading-[1.05] tracking-tight sm:text-6xl">
            One room for the band.
          </h1>
          <p className="max-w-2xl text-lg text-muted sm:text-xl">
            A calendar that finds practice days by itself, the demos you&apos;re working on, and the talk
            around them. No more &ldquo;can everyone do Thursday?&rdquo;
          </p>
          <div className="flex flex-wrap items-center gap-3">
            <Link href="/register" className={buttonPrimary}>Start your room</Link>
            <Link href="/login" className={buttonOutline}>Sign in</Link>
          </div>
          <p className="text-sm text-faint">Set up in a minute. The band joins with one link or a QR code in the rehearsal room.</p>
        </section>

        <section className="space-y-8">
          <div className="max-w-2xl space-y-3">
            <Eyebrow>01 — the problem</Eyebrow>
            <h2 className="font-display text-2xl font-semibold tracking-tight sm:text-3xl">
              Scheduling a practice is a coordination problem nobody owns
            </h2>
            <p className="text-muted">Every band has the same three frictions, and they live in three different apps.</p>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            {[
              ["Finding a day is manual labour.", "Someone asks the group chat. Three answer, one doesn't, the thread dies, and practice doesn't happen."],
              ["The music is scattered.", "Riffs in voice memos, a bounce in Messenger, lyrics in a notes app. Six months later nobody can find the second version of that bridge."],
              ["The talk has no home.", "Decisions about songs, gigs and gear land between memes, impossible to find again and disconnected from what they were about."],
            ].map(([title, body]) => (
              <div key={title} className="space-y-1.5 rounded-xl border border-line bg-surface p-5">
                <h3 className="font-medium">{title}</h3>
                <p className="text-sm text-muted">{body}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="space-y-8">
          <div className="max-w-2xl space-y-3">
            <Eyebrow>02 — how it works</Eyebrow>
            <h2 className="font-display text-2xl font-semibold tracking-tight sm:text-3xl">
              The band already knows when it can&apos;t play. Bandroom does the rest.
            </h2>
            <p className="text-muted">
              Thirty seconds once, exceptions when life happens, and a tap to confirm. Silence finally means something.
            </p>
          </div>
          <div className="grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)] lg:items-start">
            <ol className="space-y-5">
              {[
                ["Set your weekly pattern, once.", "“Evenings that generally work for me: mon, tue, thu, fri.” That's the whole setup."],
                ["Block the days life takes.", "“Can't the 12th–15th.” Nobody fills in a calendar every week; you only report what changed."],
                ["The finder proposes. You confirm.", "It ranks the days enough of you are free, soonest first. It never books anyone — an RSVP is a fact, a computed day is a guess."],
                ["It lands where you already look.", "Confirmed practices show up in your own Google or Apple calendar, with a reminder the evening before."],
              ].map(([title, body], i) => (
                <li key={title} className="flex gap-4">
                  <span className="pt-0.5 font-mono text-xs text-accent">0{i + 1}</span>
                  <div className="space-y-1">
                    <h3 className="font-medium">{title}</h3>
                    <p className="text-sm text-muted">{body}</p>
                  </div>
                </li>
              ))}
            </ol>
            <WeekGrid />
          </div>
        </section>

        <section className="space-y-8">
          <div className="max-w-2xl space-y-3">
            <Eyebrow>03 — demos</Eyebrow>
            <h2 className="font-display text-2xl font-semibold tracking-tight sm:text-3xl">
              Song ideas hold the versions. Comments pin to the second.
            </h2>
            <p className="text-muted">
              Record in the voice memo app you already trust and upload. &ldquo;Bridge idea v3&rdquo; beats fourteen files called
              new_song_final_2. Say &ldquo;1:23, the chorus should hit harder&rdquo; right on the waveform, so the demo is a
              conversation instead of a dropbox.
            </p>
          </div>
          <div className="max-w-2xl">
            <PlayerMock />
          </div>
        </section>

        <section className="space-y-8">
          <div className="max-w-2xl space-y-3">
            <Eyebrow>04 — the rules it keeps</Eyebrow>
            <h2 className="font-display text-2xl font-semibold tracking-tight sm:text-3xl">Built for a band, not for a band's paperwork</h2>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            {[
              ["No news is good news.", "You record exceptions, not attendance. Anything that needs weekly homework from four people is dead on arrival, so nothing here does."],
              ["The finder never books.", "It proposes, people confirm. Every practice gets a round of taps before it's real, and that's what makes the low-effort model safe."],
              ["The band is the unit.", "Everything is scoped to the band: members, demos, the one room. Later, one member pays and the whole band is covered. Nobody's flaky drummer blocks adoption."],
              ["Your demos stay yours.", "Hosted in the EU, no third-party tracking, and unreleased takes are never used to train anything. Being the respectful option is the point."],
            ].map(([title, body]) => (
              <div key={title} className="space-y-1.5 rounded-xl border border-line bg-surface p-5">
                <h3 className="font-medium">{title}</h3>
                <p className="text-sm text-muted">{body}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="space-y-5 rounded-xl border border-line bg-surface p-6 sm:p-10">
          <h2 className="font-display text-2xl font-semibold tracking-tight sm:text-3xl">Give the band one room.</h2>
          <p className="max-w-xl text-muted">
            Free while in early access. One of you sets it up, the rest join with a link. It works on the phone you
            have, in the hallway outside the rehearsal room.
          </p>
          <div className="flex flex-wrap gap-3">
            <Link href="/register" className={buttonPrimary}>Start your room</Link>
            <Link href="/login" className={buttonOutline}>Sign in</Link>
          </div>
        </section>
      </main>

      <footer className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-line py-6 font-mono text-[11px] text-faint">
        <Wordmark className="text-ink" />
        <span>a Tiny Giants product · made in Denmark · hosted in the EU</span>
        <span className="ml-auto">
          <Link href="/login" className="hover:text-ink">sign in</Link>
        </span>
      </footer>
    </div>
  );
}
