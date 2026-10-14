import { defineConfig } from "@playwright/test";

// L'application est publiée en Release (dotnet publish) puis servie en statique, comme en production.
// Voir ../../README.md : `./run-e2e.sh` publie puis lance ces tests.
const port = Number(process.env.LETTRA_POC_PORT ?? 5080);
const dossier = process.env.LETTRA_PUBLISH_DIR ?? "../../artifacts/publish/wwwroot";

export default defineConfig({
  testDir: "./specs",
  timeout: 120_000,
  fullyParallel: false,
  workers: 1,
  reporter: [["list"], ["json", { outputFile: "results/resultats.json" }]],
  use: {
    baseURL: `http://localhost:${port}`,
    viewport: { width: 1400, height: 800 },
    launchOptions: process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
  },
  webServer: {
    command: `npx serve -s -L -l ${port} ${dossier}`,
    url: `http://localhost:${port}`,
    reuseExistingServer: true,
  },
});
