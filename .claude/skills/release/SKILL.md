---
name: release
description: >-
  Cut and publish a new release of Confluence Page Exporter — bump the version
  everywhere, roll the CHANGELOG, tag, and let CI build and publish the GitHub
  Release. Use whenever the user wants to ship/cut/publish a release or bump the
  version: "зарелизить", "выпустить версию", "сделать релиз 2.19.0", "release
  this", "ship a new version", or right after a batch of user-facing changes has
  landed on master and should go out. Handles the full ritual so nothing (version
  strings, bilingual CHANGELOG, tag) drifts out of sync.
---

# Release Confluence Page Exporter

## The model (read first)

The **git tag `vX.Y.Z` is the source of truth for the released build** —
`.github/workflows/release.yml` triggers on `push` of a `v*` tag, extracts the
version from the tag name, and publishes self-contained single-file binaries for
`win-x64`, `linux-x64`, `osx-arm64` plus a GitHub Release with auto-generated
notes. Nothing you build locally is published; the tag drives everything.

Even so, keep every in-repo version string in sync with the tag so the source is
honest (a README that says `v2.16.0` for a `v2.18.0` tool misleads users). This
skill bumps all five places and, if they had drifted, this run silently corrects
them.

**Pushing the tag is irreversible and outward-facing** — it publishes a public
GitHub Release. Do the commit/PR/merge first; tag only at the very end, after
confirming the version with the user.

## Preconditions

1. On `master`, clean tree, up to date with origin.
2. Tests green. Run them (xunit.v3 on MTP — `dotnet test` prints nothing useful):
   ```
   dotnet build tests/ConfluencePageExporter.Tests/ConfluencePageExporter.Tests.csproj
   tests/ConfluencePageExporter.Tests/bin/Debug/net10.0/ConfluencePageExporter.Tests.exe
   ```
3. `CHANGELOG.md` has a non-empty `## [Unreleased]` section. If it's empty or
   missing, there is nothing user-facing to release — stop and ask the user.

## Choose the version

Infer the next semver bump from what's under `[Unreleased]`, then **confirm with
the user before tagging**:

- new features / commands / flags (`### Добавлено`) → **minor** (`2.18.0 → 2.19.0`)
- only fixes (`### Исправлено`) → **patch** (`2.18.0 → 2.18.1`)
- breaking changes → **major**

History is almost all minor bumps with the occasional patch (e.g. `2.13.1`).

## Steps

Let `X.Y.Z` be the new version and `DATE` today's date as `YYYY-MM-DD`.

1. **Branch:** `git checkout -b chore/release-X.Y.Z`
2. **Edit five files** (all in one commit):
   - `CHANGELOG.md` — rename the heading `## [Unreleased]` → `## [X.Y.Z] — DATE`.
   - `CHANGELOG.en.md` — same rename, mirroring the existing entry format exactly
     (same `## [X.Y.Z] — DATE` shape). Per the bilingual rule both CHANGELOGs move
     together.
   - `Directory.Build.props` — `<Version>OLD</Version>` → `<Version>X.Y.Z</Version>`.
   - `README.md` — first line `# Confluence Page Sync vOLD` → `vX.Y.Z`.
   - `README.en.md` — first line, same bump.
3. **Commit** (message in Russian, per project convention):
   ```
   chore(release): версия X.Y.Z

   <2–4 line summary of the release highlights, taken from the CHANGELOG entry>

   Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
   ```
4. **PR + self-merge** (the user's standing workflow — don't wait for review):
   `git push -u origin chore/release-X.Y.Z`, `gh pr create --base master ...`
   (Russian title/body), then `gh pr merge <n> --merge --delete-branch`.
5. **Sync master:** `git checkout master && git pull --ff-only`.
6. **Tag on the merge commit and push — this publishes the release:**
   ```
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
7. **Verify CI:** `gh run watch` (or `gh run list --workflow=Release`) until green,
   then `gh release view vX.Y.Z` to confirm the three archives attached.

## Notes / gotchas

- **Date must be real** — use today's actual date, never a placeholder.
- **osx-arm64 ships uncompressed** on purpose (`EnableCompressionInSingleFile=false`
  in `release.yml`) — a single-file/ARM extraction bug. Don't "fix" it.
- No CHANGELOG entry is needed *for the release commit itself* — rolling the
  section IS the change.
- If a release build fails after the tag is pushed, delete the tag
  (`git push --delete origin vX.Y.Z`), fix, and re-tag — the Release is only
  created after all three builds succeed (`fail_on_unmatched_files: true`).
