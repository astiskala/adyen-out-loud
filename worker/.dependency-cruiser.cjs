/**
 * Dependency-boundary rules for the Worker (see docs/architecture.md).
 *
 *   entrypoint (index.ts, relay-object.ts)
 *       |
 *       v
 *   domain (adyen/*, identity.ts)
 *
 * Domain/parser modules must stay pure enough to unit test without any Cloudflare global —
 * they must never import the entrypoint, the Durable Object, or a `cloudflare:*` built-in.
 */
/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: "no-circular",
      severity: "error",
      comment: "Circular imports make the ingress -> domain dependency direction impossible to reason about.",
      from: {},
      to: { circular: true },
    },
    {
      name: "domain-does-not-import-cloudflare-infrastructure",
      severity: "error",
      comment:
        "src/adyen/** and src/identity.ts are pure parsing/correlation logic and must run in a plain test host.",
      from: { path: "^src/(adyen/.+|pairing)\\.ts$" },
      to: { path: "^(cloudflare:|src/index\\.ts$|src/relay-object\\.ts$)" },
    },
    {
      name: "parsers-do-not-import-the-entrypoint",
      severity: "error",
      comment: "A parser importing the Worker entrypoint would be a sign the layering has inverted.",
      from: { path: "^src/adyen/.+-parser\\.ts$" },
      to: { path: "^src/index\\.ts$" },
    },
    {
      name: "production-does-not-import-tests",
      severity: "error",
      comment: "Test-only helpers and fixtures must never end up in the deployed bundle.",
      from: { path: "^src/" },
      to: { path: "^test/" },
    },
    {
      name: "no-orphans",
      severity: "warn",
      comment: "A module nothing imports is either dead code or missing from the dependency graph.",
      from: {
        orphan: true,
        pathNot: "^(vitest\\.config\\.ts|eslint\\.config\\.js|worker-configuration\\.d\\.ts)$",
      },
      to: {},
    },
  ],
  options: {
    tsPreCompilationDeps: true,
    tsConfig: { fileName: "tsconfig.json" },
    enhancedResolveOptions: {
      exportsFields: ["exports"],
      conditionNames: ["import", "require", "node", "default"],
    },
    reporterOptions: {
      text: { highlightFocused: true },
    },
  },
};
