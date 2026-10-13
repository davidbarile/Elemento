# Elemento rules engine (TypeScript)

This directory is the dependency-free TypeScript port of `Assets/Scripts/Domain` and its network authority/view layer. It uses Deno-compatible ESM and explicit `.ts` import extensions.

Run the six C# golden fixtures with Node 22:

```sh
node --experimental-strip-types Supabase~/functions/_shared/rules/golden.test.ts
```

Or with Deno when available:

```sh
deno test --allow-read Supabase~/functions/_shared/rules/golden.test.ts
```

Import the public surface from `mod.ts`. Nested DTOs deliberately retain the C# `NetJson` wire contract: PascalCase field names, numeric enum values, and explicit null fields.
