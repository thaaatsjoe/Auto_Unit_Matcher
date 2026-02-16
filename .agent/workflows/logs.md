---
description: "USE THIS when user mentions: logs, check logs, what happened, debug, diagnose, errors, crash, matching failure, app run, test results. Knows exact log file location and format for AUM application."
---

# AUM Log Reader Agent

> **MANDATORY**: Follow these steps IN ORDER. Do NOT search for log files yourself.
> The log location and format are documented below. Read this workflow completely before taking any action.
> If the user says anything like "check the logs", "what happened", "read the logs", or "I just ran the app" — this workflow tells you exactly what to do.

## Log Location — DO NOT SEARCH, USE THIS PATH

```
C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Auto_Unit_Matcher\src\AUM.UI\bin\Release\net8.0-windows\logs
```


## Step 1: Identify the Newest Log File

// turbo
1. List all files in the logs directory using `list_dir`.
2. Log files follow the naming convention `aum-YYYYMMDD.log` with rollover suffixes like `_001`, `_002`, etc.
3. The file with the **most recent date in its filename** is the newest. If multiple files share the same date, the one with the **highest numeric suffix** (e.g., `_002` > `_001` > no suffix) is the newest.
4. **Always select the newest file** — that is where the most current data lives.

## Step 2: Read the Bottom of the Log

The most recent activity is always at the **bottom** of the file. Follow this strategy:

1. First, use `view_file` **without** `StartLine`/`EndLine` to get the total line count.
2. Then read the **last 200 lines** of the file (or fewer if the file is shorter) using `view_file` with `StartLine` set to `(total_lines - 200)` and `EndLine` set to `total_lines`.
3. If more context is needed, read additional chunks working **upward** from the bottom.

## Step 3: Find the Most Recent Application Run

Each application run is bounded by these markers:
- **Start**: A line containing `[INF] Application starting`
- **End**: A line containing `[INF] Application shutting down`

To find the most recent run:
1. Scan from the bottom of the file **upward** looking for `[INF] Application starting`.
2. Everything between that `Application starting` line and the end of the file (or the next `Application shutting down`) is the most recent run.
3. If the file ends without `Application shutting down`, the application may still be running or may have crashed.

## Step 4: Be Time-Aware

- Log timestamps use the format: `YYYY-MM-DD HH:mm:ss.fff TZ` (e.g., `2026-02-14 15:33:48.822 -08:00`).
- The current time is provided by the system. Compare log timestamps against the current time to determine how recent the data is.
- Report timestamps in a human-readable way (e.g., "The last run was 10 minutes ago" or "The app crashed at 3:30 PM today").

## Step 5: Analyze and Report

When reporting, focus on **actionable information** at the appropriate level:

### Log Levels (priority order)
| Level | Tag | Meaning |
|-------|-----|---------|
| Fatal | `[FTL]` | Application crashed — **always report these** |
| Error | `[ERR]` | Something failed — **always report these** |
| Warning | `[WRN]` | Potential problem — report if relevant |
| Info | `[INF]` | Normal operations — summarize, don't list every line |
| Debug | `[DBG]` | Detailed internals — only mention if diagnosing a specific issue |

### What to Report
- **Application lifecycle**: Did it start successfully? Is it still running? Did it shut down cleanly?
- **Fatal/Error entries**: Full details including the exception message and the first few lines of the stack trace.
- **Startup summary**: How many STL files were found and registered? Did the index build succeed?
- **User activity**: Login events (`Audit context set`), scans initiated, matches found or failed.
- **Timing**: How long did startup take? How long between events?

### What NOT to Report
- Do not dump hundreds of repetitive `[DBG]` lines about individual STL registrations. Summarize them (e.g., "Registered 144 STL units across N cases").
- Do not include raw file paths unless they are relevant to an error.

## Common Patterns to Watch For

| Pattern | What It Means |
|---------|---------------|
| `[FTL] Application startup failed` followed by `DllNotFoundException` | The C++ engine DLL is missing. Needs to be built and deployed. |
| `[ERR] Stage 1 voting failed` with `Index not trained` | The FAISS index was not trained after loading units. A bug in the training pipeline. |
| `Found N existing STL files to process` | Initial directory scan count at startup. |
| `Rebuilt index with N entries from database` | How many units were already in the database at startup. |
| `Audit context set: Employee=X, Station=Y` | A user logged in. |
| `Application shutting down` + `Stopped STL monitoring` | Clean shutdown. |
| No `Application shutting down` at end of file | Possible crash or app is still running. Check the timestamp of the last entry vs current time. |

---

## Partial Scan Matching — Algorithm Analysis

> **Primary use case**: The main agent will ask you to analyze logs in the context of **refining the partial scan matching algorithm**. This means you need to understand the two-stage matching pipeline and extract the right data to answer algorithm-tuning questions.

### How Matching Works (Context for the Logs Agent)

The application matches a **scanned STL file** (a partial 3D dental scan) against a library of **known reference units**. The pipeline has two stages:

1. **Stage 1 — FAISS Voting**: Each ISS keypoint's SHOT352 descriptor votes for the most similar registered units. Fast (~100ms). Produces a ranked list of candidates.
2. **Stage 2 — RANSAC + ICP Verification**: For each candidate from Stage 1, geometric alignment is verified. This produces the final confidence score.

### Matching Log Messages (Exact Patterns)

When a scan is matched, the logs produce these entries **in order**. Look for them to reconstruct a complete matching attempt:

#### Descriptor Extraction
```
[INF] Extracting ISS+SHOT352 descriptor from <scanned_file_path>
[DBG] Parsed STL with "<N>" points
[DBG] Extracted descriptor: <N> bytes
```
- **Key data**: Point count and descriptor size tell you about the scan quality. Very low point counts (< 10,000) or very small descriptors (< 1,000 bytes) may indicate a poor-quality scan.

#### Stage 1 — Voting
```
[INF] Stage 1: FAISS voting for top 10 candidates
[DBG] Stage 1: Voting for top 10 candidates
```
If voting **succeeds**:
```
[INF] Stage 1 returned <N> candidates
[INF]   Vote #1: UnitId=<ID> Score=<float> Votes=<count>
[INF]   Vote #2: UnitId=<ID> Score=<float> Votes=<count>
...
```
If voting **fails**:
```
[ERR] Stage 1 voting failed
<Exception details on following lines>
```
- **Key data**: `Score` is the vote score (higher = more keypoint matches). `Votes` is the raw vote count. Compare the top candidate's score vs the second — a large gap suggests a confident match.

#### Stage 2 — Geometric Verification
```
[INF] Stage 2: RANSAC+ICP verification for <N> candidates
[INF]   VERIFY <CaseId>: RANSAC=<inliers>/<correspondences> (<ratio%>), ICP fitness=<float>, Final=<float>%, Votes=<count>
```
- **Key metrics** (critical for algorithm tuning):
  - `RANSAC inliers/correspondences`: How many geometric correspondences survived RANSAC outlier rejection. Higher ratio = better spatial alignment.
  - `ICP fitness`: Iterative Closest Point fitness score. Lower = better surface alignment (0.0 = perfect).
  - `Final score (%)`: The combined confidence score. This is what determines whether a match is accepted.
  - `Votes`: Carried forward from Stage 1 for correlation.

If verification **fails for a candidate**:
```
[WRN] Verification failed for unit <ID>, using vote score as fallback
```

#### Pipeline Summary
```
[INF] Pipeline complete. Top match: <CaseId> at <score>%
```
Or if no candidates were found:
```
[INF] No vote results found
```

### What the Main Agent Will Ask — How to Respond

When the main agent sends you a question about matching, it will typically fall into one of these categories. Here is how to find the answer in the logs:

| Main Agent Question | What to Look For in Logs |
|---|---|
| "Why did scan X fail to match?" | Find the `Extracting` line for that file. Check if Stage 1 returned 0 candidates (`No vote results found`) or if Stage 1 failed with an error. Check if point count is abnormally low. |
| "What was the confidence for the last match?" | Find the last `Pipeline complete. Top match:` line. Report the CaseId and score. |
| "Are the RANSAC inlier ratios improving?" | Find all `VERIFY` lines in the recent run. Extract and compare the `RANSAC=X/Y (Z%)` ratios across multiple matching attempts. |
| "What does the vote distribution look like?" | Find `Vote #N:` lines. Report all candidates with their scores. Flag if the gap between #1 and #2 is small (ambiguous match). |
| "Is ICP converging properly?" | Find `ICP fitness=` values. Values near 0.0 are good. Values > 0.01 suggest poor convergence. Report the range across all matches. |
| "How big are the descriptors for partial scans?" | Find `Extracted descriptor: N bytes` lines. Partial/cut scans typically have fewer keypoints and smaller descriptors than full units. |
| "Did the index train successfully?" | Look for `Training IVF index with N units' keypoints` followed by `IVF index training complete`. If missing or followed by an error, training failed. |
| "How many units are in the index?" | Look for `Added unit N to index (total: M)` — the last occurrence gives the current count. Or look for `Rebuilt index with N entries`. |

### Correlating Requests from the Main Agent

The main agent may give you specific context like:
- A **case ID** (e.g., `2026-10593-T0258 C3`) — search for this string to find all log entries about that case
- A **file name** or path — search for it to find the descriptor extraction and matching entries
- A **time window** (e.g., "the last 5 minutes") — use the timestamps to filter
- A **unit ID** (a numeric ID like `42`) — search for `UnitId=42` in vote/verify results

When the main agent provides any of these, use `grep_search` on the log file to quickly locate the relevant entries instead of scanning manually.

### Index Health Checks

The matching pipeline depends on a healthy FAISS index. Report these index-related events:

| Log Pattern | Meaning |
|---|---|
| `Index cleared and recreated` | Index was reset (normal at startup) |
| `Rebuilt index with 0 entries from database` | Empty database — no reference units exist yet |
| `Training IVF index with N units' keypoints` | Training started |
| `IVF index training complete` | Training succeeded |
| `Index not trained — call trainIndex() first` | **BUG**: Units were added but index was never trained |
| `Added unit N to index (total: M)` | A unit was indexed. `M` is the running total |
