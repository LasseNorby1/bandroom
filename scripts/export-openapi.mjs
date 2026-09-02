// Boots the api just long enough to export /openapi/v1.json into
// packages/client/openapi.json. Startup does not touch the database, so no
// postgres is needed; hangfire is disabled via Jobs__Enabled.
import { spawn } from "node:child_process";
import { writeFile } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";

const url = "http://localhost:5180/openapi/v1.json";
const out = new URL("../packages/client/openapi.json", import.meta.url);

const api = spawn("dotnet", ["run", "--project", "src/Bandroom.Api", "--no-build"], {
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    Jobs__Enabled: "false",
  },
  stdio: "ignore",
});

try {
  let document = null;
  for (let attempt = 0; attempt < 60 && !document; attempt++) {
    await delay(1000);
    try {
      const response = await fetch(url);
      if (response.ok) {
        document = await response.text();
      }
    } catch {
      // still booting
    }
  }

  if (!document) {
    throw new Error("api did not serve the openapi document within 60s — run `dotnet build` first?");
  }

  await writeFile(out, document);
  console.log(`wrote ${out.pathname}`);
} finally {
  api.kill();
}
