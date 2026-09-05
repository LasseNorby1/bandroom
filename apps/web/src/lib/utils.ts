/// shadcn's conventional import path. The helper itself lives in ./cn (the
/// app imports it from there); this re-export keeps `shadcn add` output
/// compiling without a per-file import rewrite.
export { cn } from "./cn";
