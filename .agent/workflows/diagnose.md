---
description: "USE THIS when tests fail or matching results are wrong. Chains /sync → /deep-think → /logs → /audit in sequence for systematic root cause analysis."
---

# Diagnose Protocol

When test results are wrong or the user reports failures, run these phases IN ORDER.
Each phase must complete before the next begins.

## Phase 1: Sync (/sync)

// turbo-all

1. Update all brain documents (Brain 2, 7 especially) with latest:
   - Current pipeline parameters and architecture
   - Ground truth test cases and expected results
   - Any new bugs discovered
   - Technical decisions and their rationale
2. Update this and other workflows if terminology has changed
3. Ensure all documentation reflects the ACTUAL code, not the planned code

## Phase 2: Deep Think (/deep-think)

Answer all 5 gates from the deep-think protocol:

1. **Gate 1:** What exactly failed? State symptoms from user + logs
2. **Gate 2:** Read the actual source code of the changed files. Do NOT work from memory
3. **Gate 3:** List 3+ possible root causes, evaluate each against:
   - Does it explain ALL the symptoms?
   - Does it work at 20,000 units?
   - Does it work for partial scans?
4. **Gate 4:** Check constraints (scale, UNC paths, silent failures, breaking changes)
5. **Gate 5:** Write the fix plan BEFORE implementing

## Phase 3: Logs (/logs)

1. Find the latest log file:
   ```powershell
   Get-ChildItem "C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Auto_Unit_Matcher\src\AUM.UI\bin\Release\net8.0-windows\logs" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
   ```
2. Check for errors: `Select-String -Pattern "\[ERR\]|\[WRN\]" -Path <logfile>`
3. Check Stage 1 timing: `Select-String -Pattern "Stage 1" -Path <logfile>`
4. Check vote results for the correct unit IDs:
   - `0 - Copy - Copy.stl` → should match UnitId=101 (2026-11322-R743 A2)
   - Check if UnitId=101 appears in top-30 vote results
5. Check Stage 2 RANSAC + ICP results:
   - Look for DenseICP fitness = FLT_MAX (340282....) → indicates ICP divergence
   - Look for RANSAC <12 inliers → correspondence starvation
6. Extract key metrics into a summary table

## Phase 4: Audit (/audit)

1. Self-audit every code change made since the last working state
2. For each change, verify:
   - Does the change match the intended fix?
   - Could the change have unintended side effects?
   - Is the math correct? Work through actual numbers
3. Compare pre-change vs post-change log results
4. Write findings to diagnosis_report.md artifact

## After All Phases

- Update task.md with findings
- Present findings to user via notify_user with:
  - Root cause summary
  - Proposed fix with confidence level
  - Request approval before implementing
