---
description: "USE THIS at conversation start or when encountering unfamiliar territory. Run when: new conversation, first message, getting started, I don't understand, research, investigate, look into, how does this work, what is, explore, unfamiliar code."
---

# Research Protocol

## At Conversation Start

1. **Read brain documents.** Always check existing knowledge before doing anything:
   - `docs/01-architecture.md`: Architecture (where does code live?)
   - `docs/02-algorithm.md`: Matching Algorithm — ISS+SHOT352+FAISS Voting+RANSAC+ICP
   - `docs/03-data-flow.md`: Data Flow (registration, matching, handle lifecycle)
   - `docs/04-database.md`: Database schema and repositories
   - `docs/05-known-issues.md`: Known Issues & Design Gotchas
   - `docs/06-configuration.md`: Configuration (paths, parameters, tuning)
   - `docs/07-testing.md`: Testing & Validation

2. **Check PRD status** (Brain 6). Know what's implemented vs not.

3. **Read the user's current file.** Their active editor tab is a hint about what they're working on.

## Before Any Algorithm Change — Web Research Protocol

> **MANDATORY**: Do NOT implement algorithm changes based on intuition alone. Follow this research protocol FIRST.
> The user is not a programmer — they rely entirely on you to find the best approaches. Getting this wrong wastes their time and yours.

### Step 1: Search Local Knowledge First

4. **Read relevant brain documents** (already loaded in Step 1-3 above).
5. **Check `docs/05-known-issues.md`** — has this problem been encountered before? Is there a documented reason for the current approach?

### Step 2: Search the Web

Use the `search_web` tool to find external information. Follow these rules:

**How to formulate queries:**
- Be specific to the technology. Don't search "3D matching" — search "PCL SHOT352 partial point cloud matching" or "FAISS IVF index training threshold minimum vectors."
- Include version numbers when relevant: "PCL 1.14 ISS keypoint parameters" not just "PCL ISS."
- Search for the **problem**, not the solution: "RANSAC low inlier ratio partial scan" not "how to fix RANSAC."

**Required search categories** (do ALL of these, not just one):

| Category | Example Queries | Why |
|----------|----------------|-----|
| **Academic/algorithm** | `"ISS SHOT352 partial object matching"`, `"3D local feature matching partial views"` | Find if there's a known best approach for this specific problem |
| **Library documentation** | `"PCL ISS keypoint detector parameters"`, `"FAISS IndexIVFFlat nprobe tuning"` | Find correct usage, parameter ranges, edge cases |
| **Known issues/bugs** | `"PCL SHOT352 NaN descriptors"`, `"FAISS index not trained error"` | Find if others hit the same problem and how they solved it |
| **Production systems** | `"dental CAD CAM 3D shape retrieval"`, `"industrial 3D part matching system"` | Find how real systems solve this at scale |
| **Alternatives** | `"SHOT352 vs FPFH vs 3DSC point cloud features"`, `"FAISS vs Annoy vs ScaNN"` | Ensure the current approach is actually the best one |

6. **Run at least 3 searches** across different categories before forming an opinion.

### Step 3: Read and Evaluate Sources

When `search_web` returns relevant results, use `read_url_content` to read the full pages:

7. **Read the actual documentation pages** — don't rely on search result summaries. They are often incomplete or misleading.
8. **For academic papers**, focus on:
   - The method description (Section 3 or "Method")
   - The experimental results (do they test with partial views?)
   - The limitations section (what doesn't work?)
9. **For GitHub issues/Stack Overflow**, check:
   - Is the answer accepted/verified?
   - Is it for the same library version we use?
   - Are there follow-up comments disagreeing?

### Step 4: Evaluate What You Found

10. **For each approach discovered, answer these questions:**

| Question | Why It Matters |
|----------|---------------|
| Does it work with **partial scans** (50-70% of surface)? | AUM matches cut/trimmed dental crowns against full reference models |
| Does it scale to **20,000+ units**? | AUM's production target |
| Does it work with **PCL point clouds** (PointXYZ/PointNormal)? | That's our pipeline format |
| What's the **time complexity** per query? | User expects matches in < 5 seconds |
| Has anyone used it for **dental/medical** 3D data? | Dental crowns have specific geometry properties |
| What are the **failure modes**? | Every method fails somewhere — know where |

11. **Run the math.** Before recommending any algorithm change:
    - Write out the equations
    - Work through a concrete numerical example with realistic AUM values
    - Identify edge cases (zero keypoints, identical descriptors, extremely small scans)
    - Calculate worst-case performance at 20,000 units

### Step 5: Synthesize Into Options

12. **Present at least 3 approaches** to the user with this structure:

```
### Option A: [Name]
- **How it works**: [1-2 sentence summary]
- **Evidence**: [paper/doc/issue link that supports this]
- **Pros**: [what's good]
- **Cons**: [what's bad]
- **Risk level**: [Low/Medium/High — will it break existing functionality?]
- **Effort**: [how many files/lines change]
```

---

## Deep Research Protocol — When Standard Search Isn't Enough

For complex algorithm questions where initial searches don't give clear answers, go deeper:

13. **Search for survey papers**: `"survey 3D shape retrieval local features 2023"` — these summarize the entire field and compare methods.
14. **Search for benchmark datasets**: `"3D partial shape matching benchmark"` — find standardized evaluation methods.
15. **Read library source code on GitHub**: If PCL or FAISS docs are unclear, read the actual implementation:
    - PCL: `https://github.com/PointCloudLibrary/pcl`
    - FAISS: `https://github.com/facebookresearch/faiss`
16. **Search for alternative libraries**: `"Open3D vs PCL feature extraction"`, `"point cloud matching library C++ 2024"` — maybe a better tool exists.

### Domain-Specific Search Resources

For AUM's specific tech stack, these are high-value search targets:

| Resource | URL | What to Find |
|----------|-----|-------------|
| PCL Documentation | `https://pointclouds.org/documentation/` | Feature extractor parameters, algorithm details |
| PCL Tutorials | `https://pcl.readthedocs.io/projects/tutorials/` | Working code examples for features, registration |
| FAISS Wiki | `https://github.com/facebookresearch/faiss/wiki` | Index types, training requirements, GPU options |
| FAISS Benchmarks | `https://github.com/facebookresearch/faiss/wiki/Indexing-1M-vectors` | Performance data for different index types at scale |
| Open3D Docs | `http://www.open3d.org/docs/release/` | Alternative implementations to compare against |

---

## Before Any Bug Fix

17. **Use the `/logs` workflow first.** Read the actual application logs before guessing:
    - Invoke the `/logs` workflow to find the relevant error
    - Get the full stack trace and context
    - Identify the exact line of code that failed

18. **Reproduce first.** Don't fix based on theory:
    - Run the app
    - Trigger the bug
    - Read the actual error message/log
    - Verify the fix removes the symptom

19. **Find the root cause, not the symptom:**
    - Ask "why?" five times (5 Whys technique)
    - The first "fix" that comes to mind is usually a bandaid
    - If the fix doesn't address WHY it happened, it's wrong

20. **Search the web for the exact error message:**
    - Copy the exception type and message into `search_web`
    - Include the library name: `"PCL NaN normal estimation radius too small"` not just `"NaN in normals"`
    - Check if it's a known library bug vs. a usage error

---

## Before Proposing Solutions to User

21. **Check all constraints** (see `/deep-think` Gate 4).
22. **Present multiple options** with pros/cons, not just one. Use the Option A/B/C format from Step 5.
23. **Be honest about uncertainty.** If you're not sure, say so. State what you found and what you couldn't find.
24. **Link your sources.** Include URLs from your research so the user (or a future agent) can verify.

