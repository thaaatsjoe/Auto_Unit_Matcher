---
description: "USE THIS before ANY significant implementation or design decision. Run when: planning, designing, should I, how should, best approach, architecture decision, algorithm change, refactor, big change, breaking change, tradeoff."
---

# Deep Think Protocol

Before writing ANY code that changes behavior, answer these questions IN ORDER.
Skip nothing. Write answers in the implementation plan.

## Gate 1: Do I Understand the Problem?

1. **What exactly is broken or missing?** State the symptom, not your theory.
2. **What evidence do I have?** Logs, screenshots, test output, user description.
3. **Have I reproduced it?** If not, reproduce first. Do not guess.

## Gate 2: Do I Understand the Existing System?

4. **Read the brain documents first.** Check all 7 brains for relevant context.
5. **Read the actual source code.** Not from memory. View the exact files.
6. **Trace the data flow.** For matching issues: STL → ISS keypoints → SHOT352 → FAISS voting → RANSAC+ICP → score. Where in this chain does the problem occur?

## Gate 3: Is This the Best Approach?

7. **What are at least 3 possible approaches?** List them. Don't commit to the first idea.
8. **For each approach, answer:**
   - Does it work at 20,000 units?
   - Does it work for partial scans (cut crowns)?
   - What are the failure modes?
   - What's the complexity (lines of code, files touched)?
9. **Can I prove it works mathematically?** For algorithmic changes:
   - Write the math. What happens to the numbers?
   - Work through a concrete example with actual values.
   - What happens in the WORST case, not just the average case?
10. **How does this break?** Adversarial analysis:
    - What input would make this give wrong results?
    - What happens when the network share is unavailable?
    - What happens with 0 files? 1 file? 20,000 files?
    - What happens with corrupted data?

## Gate 4: Constraints Check

11. **Check every constraint:**
    - [ ] Works at 20,000-unit scale
    - [ ] Works over UNC network paths
    - [ ] No silent failures (every error visible to user)
    - [ ] Doesn't break existing working functionality
    - [ ] Matches PRD requirements
    - [ ] Doesn't require user to understand programming

## Gate 5: Plan Before Code

12. **Write the implementation plan FIRST.**
13. **Get user approval before implementing.**
14. **If the user points out a flaw, do NOT patch around it. Go back to Gate 3.**

## After Implementation

15. **Build and verify it compiles.**
16. **Test with real data, not just test data.**
17. **Update the brain documents with any new knowledge.**
