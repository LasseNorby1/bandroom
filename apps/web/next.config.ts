import path from "node:path";
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Standalone output for the production container; tracing rooted at the
  // workspace so the monorepo's hoisted deps are included.
  output: "standalone",
  outputFileTracingRoot: path.join(__dirname, "../../"),
  transpilePackages: ["@bandroom/client"],
};

export default nextConfig;
