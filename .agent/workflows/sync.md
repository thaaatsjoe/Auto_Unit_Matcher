---
description: "USE THIS to check if docs/workflows are outdated. Run when: sync, update docs, stale documentation, is this still accurate, docs out of date, after algorithm change, after refactor, after adding exports, periodic health check."
---

# Sync — Documentation & Workflow Watchdog

Audits all project documentation and agent workflows against the actual source code. Produces a drift report listing what's stale and needs updating. **Does not auto-fix** — the main agent and user decide what to change.

---

## Step 1: Discover All Monitored Files

Do NOT rely on a hardcoded file list. New documents and workflows can be added at any time. **Dynamically discover** every file to audit:

// turbo
1. List all `.md` files in `.agent/workflows/` — these are agent workflows.
// turbo
2. List all `.md` files in `docs/` — these are brain documents.
// turbo
3. Check for `PRD.md` in the project root.
// turbo
4. Check for `README.md` in the project root.
// turbo
5. List any other `.md` files in the project root (new docs may appear).

Compile the full list. If there are new files you haven't seen before, **include them in the audit** — their existence itself may be noteworthy.

---

## Step 2: Snapshot the Source of Truth

Before checking docs, read the actual source code to build a current-state reference. Focus on these key source files:

### C++ Engine Exports (Interop Contract)
// turbo
6. Read `src/AUM.Engine/include/exports.h` — This is the **C/C++ API boundary**. Every exported function, handle type, error code, and struct defined here is the contract between C++ and C#. Changes here ripple into `01-architecture.md`, `NativeMethods.cs`, and all handle wrapper classes.

### C# Matching Pipeline
// turbo
7. Read `src/AUM.Core/Engine/FingerprintEngine.cs` — Current algorithm name, version string, method signatures.
// turbo
8. Read `src/AUM.Core/Services/MatchingService.cs` — Current pipeline stages, log messages, scoring logic.

### C# DI Container
// turbo
9. Read `src/AUM.Core/DependencyInjection/ServiceCollectionExtensions.cs` — Current service registrations and lifetimes.

### Log Format
// turbo
10. Read the last 200 lines of the newest log file in `src/AUM.UI/bin/Release/net8.0-windows/logs/` — Current log message patterns.

---

## Step 3: Run Drift Checks

For **each monitored document**, read it and check against the source-of-truth snapshot. Report drift in these categories:

### Category 1: Algorithm / Pipeline Drift
Check brain docs and workflows for references to algorithm components that have changed.

| What to Check | How to Detect |
|---|---|
| Feature descriptor type | Does the doc say `FPFH` when code uses `SHOT352`? Does it say `ISS` vs some other keypoint method? |
| Pipeline stage names | Does the doc describe stages (e.g., "Codebook + BoW") that no longer exist in the code? |
| Scoring formula | Does the doc describe a scoring method (e.g., "matchRatio × qualityFactor") that differs from the actual `Verify()` output? |
| Stage count | Does the doc say "2-stage" when there are now 3 stages, or vice versa? |
| Algorithm parameters | Are magic numbers (radii, thresholds, cluster counts) in docs still matching the code? |

### Category 2: File / Class / Namespace Drift
Check `01-architecture.md` and other structural docs.

| What to Check | How to Detect |
|---|---|
| Source file existence | Does the doc reference `.cpp/.h/.cs` files that no longer exist? Use `find_by_name` to verify. |
| Class/method names | Does the doc reference classes or methods that have been renamed? Use `grep_search` on the codebase. |
| DI registrations | Does the doc list services with lifetimes (Singleton/Scoped) that differ from `ServiceCollectionExtensions.cs`? |
| Namespace paths | Does the doc describe a namespace layout that doesn't match `list_dir` output? |

### Category 3: C++ Exports Drift
Check docs and C# interop code against `exports.h`.

| What to Check | How to Detect |
|---|---|
| Exported function count | Does `01-architecture.md` say "28 C-API functions" but `exports.h` has more or fewer? Count the `AUM_API` declarations. |
| Handle types | Are handle types in docs (e.g., `AUM_CodebookHandle`) still present in `exports.h`? Have new ones been added? |
| Error codes | Does the `AUM_ErrorCode` enum in docs match the actual enum in `exports.h`? |
| Struct definitions | Do result structs (e.g., `AUM_VoteResult`, `AUM_VerificationResult`) match between docs and code? |

### Category 4: Log Format Drift
Check `logs.md` workflow against actual log messages in source code.

| What to Check | How to Detect |
|---|---|
| Log message patterns | Are the exact log strings in `logs.md` still present in `_logger` calls in the source? Use `grep_search` for key phrases like `"Stage 1"`, `"VERIFY"`, `"Pipeline complete"`. |
| New log messages | Are there `_logger` calls in the source that `logs.md` doesn't document? These represent blind spots. |
| Log levels | Has a message changed from `LogInformation` to `LogError` or vice versa? |

### Category 5: Configuration Drift
Check `06-configuration.md` and settings references.

| What to Check | How to Detect |
|---|---|
| Config file paths | Do documented paths (database, codebook, index) match actual `App.xaml.cs` or `appsettings.json`? |
| Settings keys | Are settings keys in docs still used in code? Use `grep_search`. |
| Default values | Do documented defaults match actual code defaults? |

### Category 6: Workflow Cross-Reference Drift
Check workflows that reference each other or reference brain docs.

| What to Check | How to Detect |
|---|---|
| Brain doc numbering | `research.md` references "Brain 1", "Brain 2", etc. Do these numbers still match the actual filenames in `docs/`? |
| Pipeline references | `deep-think.md` Gate 2 describes a data flow. Does it match the current pipeline? |
| Log patterns in logs.md | Do the "Common Patterns" and "Matching Log Messages" match current source? |

---

## Step 4: Produce the Drift Report

Format the report clearly. For each finding:

```
### [DRIFT] <Document Name> — <Category>

**What the doc says**: <quote or paraphrase from the document>
**What the code actually does**: <evidence from source>
**Impact**: <who/what is affected if this isn't fixed>
**Suggested fix**: <brief description of what should change>
```

### Severity Levels

| Severity | Meaning | Example |
|----------|---------|---------|
| 🔴 **CRITICAL** | Document actively teaches wrong information. Agents relying on it will make incorrect decisions. | Brain 2 describing FPFH when code uses SHOT352 |
| 🟡 **WARNING** | Document is incomplete or partially stale. Still usable but missing recent changes. | New log messages not documented in `logs.md` |
| 🟢 **INFO** | Minor discrepancy. Low risk of confusion. | A file was renamed but the old name is still understandable |
| ✅ **OK** | Document is in sync with the code. | No action needed |

### Report Summary

End the report with a summary table:

```
| Document | Status | Findings |
|----------|--------|----------|
| docs/01-architecture.md | 🟡 WARNING | 2 findings |
| docs/02-algorithm.md | 🔴 CRITICAL | 3 findings |
| .agent/workflows/logs.md | ✅ OK | 0 findings |
| ... | ... | ... |
```

---

## Step 5: Recommend Next Steps

After the drift report, recommend specific actions:

1. **Which documents to update first** — prioritize 🔴 CRITICAL items
2. **Which workflows need revision** — if a workflow references stale brain docs, it inherits that staleness
3. **Whether new brain docs are needed** — if significant new functionality has no documentation at all
4. **Whether any workflows should be created** — if there are repeated tasks that don't have a workflow yet

---

## When to Run This Workflow

- **After any algorithm change** (new feature descriptors, scoring changes, pipeline restructuring)
- **After adding or removing C++ exports** (changes to `exports.h`)
- **After adding new services or changing DI registrations**
- **After changing log messages** (new `_logger` calls, changed log levels)
- **Periodically** — at least once per major development milestone
- **When the user asks** — via `/sync`
