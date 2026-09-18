// @ts-check
import js from "@eslint/js";
import tseslint from "typescript-eslint";
import eslintConfigPrettier from "eslint-config-prettier";
import jsdoc from "eslint-plugin-jsdoc";

const jsdocPlugin = {
  plugins: { jsdoc },
  rules: jsdoc.configs.recommended.rules,
};

export default tseslint.config(
  {
    ignores: ["dist/**", "node_modules/**", "coverage/**", ".wrangler/**", "worker-configuration.d.ts"],
  },
  js.configs.recommended,
  {
    // Plain config files run under Node, not the Workers runtime, and aren't part of tsconfig.json's
    // project (which only includes src/test) — so they get untyped linting only.
    files: ["*.js", "*.mjs"],
    languageOptions: { sourceType: "module" },
  },
  {
    files: ["*.cjs"],
    languageOptions: {
      sourceType: "commonjs",
      globals: { module: "writable", require: "readonly", __dirname: "readonly" },
    },
  },
  {
    files: ["src/**/*.ts", "test/**/*.ts", "vitest.config.ts"],
    extends: [...tseslint.configs.strictTypeChecked, ...tseslint.configs.stylisticTypeChecked],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      "@typescript-eslint/no-unnecessary-condition": ["error", { allowConstantLoopConditions: true }],
      // These fire on legitimate patterns this codebase relies on (e.g. `satisfies ExportedHandler<Env>`,
      // Durable Object SQL row shapes intersected with Record<string, SqlStorageValue>). Correctness and
      // security-relevant rules stay on; only style-flavored strictness that produced noise is relaxed.
      "@typescript-eslint/no-non-null-assertion": "off",
      "@typescript-eslint/restrict-template-expressions": [
        "error",
        { allowNumber: true, allowBoolean: true },
      ],

      // Promise/async correctness — the highest-signal category for a Worker that fans out into
      // `ctx.waitUntil`, alarms, and WebSocket handlers where a silently dropped rejection is a real bug.
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-misused-promises": "error",
      "@typescript-eslint/require-await": "error",

      // Unused code and unsafe `any` leakage are correctness signals, not style.
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      "no-console": ["warn", { allow: ["error"] }],

      // JSDoc enforcement for exported APIs.
      "jsdoc/require-jsdoc": [
        "error",
        {
          publicOnly: true,
          require: {
            FunctionDeclaration: true,
            MethodDefinition: true,
            ClassDeclaration: true,
            ArrowFunctionExpression: true,
            FunctionExpression: true,
          },
          contexts: ["TSInterfaceDeclaration", "TSTypeAliasDeclaration", "TSEnumDeclaration"],
        },
      ],
      "jsdoc/require-param": "error",
      "jsdoc/require-returns": "error",
      "jsdoc/require-param-description": "error",
      "jsdoc/require-returns-description": "error",
    },
  },
  {
    files: ["test/**/*.ts"],
    rules: {
      // Test fixtures intentionally build malformed/partial payloads to exercise parser rejection paths.
      "@typescript-eslint/no-unsafe-assignment": "off",
      "@typescript-eslint/no-unsafe-member-access": "off",
      // Tests don't need JSDoc on test functions.
      "jsdoc/require-jsdoc": "off",
    },
  },
  eslintConfigPrettier,
  jsdocPlugin,
);
