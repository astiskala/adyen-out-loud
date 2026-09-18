import { cloudflareTest } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [cloudflareTest({ wrangler: { configPath: "./wrangler.jsonc" } })],
  test: {
    include: ["test/**/*.test.ts"],
    coverage: {
      // Native V8 coverage cannot observe code running inside the Workers runtime (workerd);
      // Istanbul instrumentation is Cloudflare's documented alternative for @cloudflare/vitest-plugin.
      provider: "istanbul",
      include: ["src/**/*.ts"],
      exclude: ["src/index.ts"],
      // "lcov" is additive to the existing local-dev reporters above — SonarCloud's JS/TS analysis
      // (.github/workflows/sonarcloud.yml) ingests coverage/lcov.info; nothing else reads it.
      reporter: ["text", "text-summary", "html", "json-summary", "lcov"],
      thresholds: {
        // src/adyen/** is the critical pure logic (Display parsing, terminal-serial derivation);
        // src/relay-object.ts is Durable Object transport wiring around that logic, held to the
        // general floor. src/index.ts (thin HTTP routing) is exercised by worker.test.ts but excluded
        // from the gate itself — it is almost entirely branches already covered end-to-end.
        "src/adyen/**": { statements: 90, branches: 85, functions: 90, lines: 90 },
        "src/relay-object.ts": { statements: 80, branches: 75, functions: 80, lines: 80 },
      },
    },
  },
});
