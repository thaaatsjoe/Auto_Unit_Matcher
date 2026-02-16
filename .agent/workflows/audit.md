---
description: "USE THIS after completing any implementation. Run when: done, finished, code review, quality check, self-audit, verify work, check my work, is this correct, sanity check, before merging."
---

# Self-Audit Protocol

After completing any implementation or fix, run this audit.

## Code Quality Audit

- [ ] **No silent failures.** Every catch block either:
  - Shows a MessageBox to the user, OR
  - Logs at Error level with enough context to diagnose, OR
  - Re-throws to a handler that does one of the above
- [ ] **No hardcoded assumptions.** Check for:
  - Hardcoded paths (should use config)
  - Hardcoded limits (should scale with data)
  - Hardcoded timeouts (should be configurable)
- [ ] **Every error message is useful.** A non-programmer should understand what went wrong and what to do about it.
- [ ] **Resource cleanup.** Every `new` has a corresponding `Dispose`/`delete`. Every handle is wrapped in `using` or `SafeHandle`.

## Architecture Audit

- [ ] **Does this belong in the right layer?**
  - C++ Engine: computation, algorithms, PCL/FAISS
  - C# Core: business logic, data access, services
  - C# UI: presentation, user interaction only
- [ ] **Does this follow existing patterns?** Check similar code in the same layer.
- [ ] **Is the DI lifetime correct?** Singleton vs Scoped vs Transient.

## Scale Audit

- [ ] **What happens at 20,000 units?**
  - Memory: does this load all 20K descriptors into RAM?
  - Time: is this O(n²) when O(n log n) is possible?
  - Network: does this make N network calls when 1 would do?
- [ ] **What happens on first run?** (Empty database, no index)
- [ ] **What happens on network timeout?** (UNC share unavailable)

## Knowledge Audit

- [ ] **Did I learn anything new?** If yes, update the relevant brain document.
- [ ] **Did this fix a bug?** If yes, add to Brain 7 (bugs/decisions) with root cause.
- [ ] **Did constraints change?** If yes, update Brain 7.
- [ ] **Did PRD status change?** If yes, update Brain 6.
