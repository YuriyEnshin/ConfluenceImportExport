---
name: bump-normalization-epoch
description: >-
  Update the content-normalization contract after changing how Confluence
  storage-format is canonicalized or hashed, so persisted marker hashes don't
  silently mismatch. Use whenever you edit XmlContentNormalizer,
  RegexContentNormalizer, the HtmlEntities table, attribute-sort / whitespace /
  volatile-artifact-stripping rules, the SHA-256 hashing, or switch the active
  IContentNormalizer — or the moment ContentHasherTests.Golden_* fails. Keeps
  NormalizationContract.CurrentEpoch and GoldenNormalized in lockstep. Trigger on
  "нормализатор", "normalization changed", "golden vector failing", "bump epoch",
  or any diff touching Services/*Normalizer*.cs or the hash recipe.
---

# Bump the normalization contract (epoch + golden)

## Why this exists

The normalized storage format of each page is hashed (SHA-256) and stored in its
`.id*` marker (`h` = hash, `ne` = epoch). On the next sync, `ContentHasher`
recomputes the hash and compares — that's how a *real* local edit is told apart
from an mtime-only touch (editor re-save, pretty-print, copy, `touch`, VCS
checkout), eliminating false "changed locally" conflicts.

That only holds while the **recipe is stable**. If you change normalization so
its output differs, every marker written under the old recipe now hashes
differently → the tool either cries wolf on untouched pages or misses genuine
edits. The guard is a two-part contract in
[`NormalizationContract`](../../../src/ConfluencePageExporter/Services/NormalizationContract.cs):

- **`CurrentEpoch`** — stamped into new markers. A marker whose epoch ≠
  `CurrentEpoch` is distrusted and the code safely falls back to mtime comparison
  (see `ContentHasher.HasChanged`). Bumping the epoch is what actually protects
  old markers — it makes them opt out instead of silently mismatching.
- **`GoldenNormalized`** — the expected normalization of `GoldenInput`. A runtime
  self-check + the unit test
  [`ContentHasherTests.Golden_DefaultNormalizerReproducesContract`](../../../tests/ConfluencePageExporter.Tests/Services/ContentHasherTests.cs)
  fail the build the instant normalized output drifts. That red test is the
  intended tripwire that sends you here.

## When to run

Any change that can alter normalized output: `XmlContentNormalizer`,
`RegexContentNormalizer`, the `HtmlEntities` table, attribute-sort / whitespace /
volatile-artifact-stripping rules, the hash algorithm, or switching the active
`IContentNormalizer`.

## Steps

1. **Make your normalizer change first.** Build.
2. **Run the golden tripwire** — it should now fail:
   ```
   dotnet build tests/ConfluencePageExporter.Tests/ConfluencePageExporter.Tests.csproj
   tests/ConfluencePageExporter.Tests/bin/Debug/net10.0/ConfluencePageExporter.Tests.exe --filter-method "*Golden_DefaultNormalizerReproducesContract*"
   ```
   Shouldly prints the **actual** normalized string next to the expected one.
3. **Refresh `GoldenNormalized`** to the new normalized output. Two ways:
   - Derive it by hand — apply your new rule to the existing `GoldenNormalized`
     (usually the cleaner path, since the change is known and local); or
   - Copy the **actual** string from the failing assertion.
   ⚠️ `GoldenNormalized` contains a real **U+00A0 non-breaking space** (decoded
   from `&nbsp;` in `GoldenInput`), *not* an ASCII space — preserve it exactly.
   Console copies of nbsp are error-prone, so prefer deriving by hand and eyeball
   that single character. Normally you do **not** touch `GoldenInput` — it's the
   fixed canary; only `GoldenNormalized` changes.
4. **Increment `CurrentEpoch` by 1.**
5. **Add a history line** to the `CurrentEpoch` doc-comment, matching the existing
   style (`History: 1 = initial; 2 = strip ac:macro-id …; 3 = …`), describing what
   the new epoch changes.
6. **Re-run `ContentHasherTests` (and the full suite).** Golden passes;
   `Xml_RegimeTrusted_AndEpochCurrent` confirms the regime is trusted at the new
   epoch.
7. **CHANGELOG if user-visible.** An epoch bump that fixes phantom "local changed"
   diffs *is* user-facing (e.g. epoch 2 shipped in v2.12.1 to fix Confluence
   canonicalization noise) — add a `### Исправлено`/`### Изменено` entry to
   `[Unreleased]` in **both** `CHANGELOG.md` and `CHANGELOG.en.md`. A pure internal
   refactor with byte-identical output shouldn't have needed an epoch bump at all.

## Gotchas

- Bump **both** epoch and golden. The test enforces the golden; only *you* can
  enforce the epoch bump — and the epoch is the half that actually shields
  existing markers.
- Old markers aren't rewritten in place; they self-heal (distrusted → mtime
  fallback) until the next sync re-stamps them at the new epoch. That's expected.
- If you *swap* the active normalizer (e.g. to `RegexContentNormalizer`) rather
  than edit one, the same contract applies — its output must reproduce
  `GoldenNormalized` or the regime goes untrusted.

See also `.claude/rules/dotnet-maintenance.md` ("КРИТИЧНО: контракт
нормализации") and the phantom-diff history behind epochs 2–3.
