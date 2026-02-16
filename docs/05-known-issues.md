# Brain 5: Known Issues & Design Gotchas

## Active Issues

### 1. No End-to-End Testing 🔴

The ISS+SHOT352+FAISS Voting+RANSAC+ICP pipeline has been built and compiles cleanly, but **has never been tested with real data**. No accuracy benchmarks, no performance measurements, no partial scan testing.

**Risk**: Unknown — the pipeline might work perfectly or might have critical bugs in descriptor extraction, voting, or verification.

**Required before production use**:
- Delete old `aum.db` to force re-registration with new SH01 blob format
- Register sample STL files
- Test full crown matching
- Test partial/cut crown matching
- Measure Stage 1 and Stage 2 latency
- Validate confidence scores are meaningful

### 2. Index Not Persisted to Disk 🟡

The FAISS IndexIVFFlat is rebuilt from scratch on every application startup by loading all unit descriptors from SQLite and calling `trainIndex()`. With `aum_save_index` / `aum_load_index` implementations available but not wired into the C# `IndexService`, this means:

- Startup time scales with unit count × keypoints (could be slow at 10K+ units)
- The index training uses CPU-bound k-means clustering on every launch

**Mitigation**: Wire up `aum_save_index` / `aum_load_index` in `IndexService` to cache the trained index.

### 3. Index-Database Sync Risk at Runtime 🟡

New units registered after `trainIndex()` are added directly to the IVF index (vectors inserted but no retraining). If many units are added post-training:
- The cluster centroids become stale
- Search quality may degrade
- No mechanism to trigger periodic re-training

**Mitigation approaches**:
- Track post-training insert count; retrain when threshold exceeded
- Schedule background retraining during idle periods
- Log post-training insert counts for monitoring

### 4. StlMonitorService Startup Scan 🟡

`StlMonitorService` scans all existing STL files (not just new ones) on startup. With a large scanner output directory containing thousands of files, this could:
- Delay application startup
- Re-process already-registered files
- Trigger unnecessary extraction work

**Mitigation**: Check database for existing registration before processing.

### 5. ABS Lab Management Integration Stubbed 🟡

The `AbsCacheRepository` and related data structures exist but the actual integration with 3Shape ABS Lab Management is not implemented. The system cannot pull case metadata from the lab management system.

### 6. Missing PRD Features 🟡

Several features from the PRD are not yet implemented:
- Statistics dashboard
- Duplicate detection UI
- Label/tag printing
- Bulk operations
- Data export
- Advanced search filters

### 7. Stage 2 Brute-Force Correspondence Is O(n×m) 🟡

In `matching.cpp::verify()`, SHOT correspondences between query and candidate are built using brute-force L2 search over all candidate keypoints for each query keypoint. With 1000 keypoints × 1000 keypoints × 352 dimensions, this is ~352 million float operations per candidate.

For 10 candidates in Stage 2, total: ~3.5 billion float operations.

**Measured**: Not yet benchmarked. Could be a bottleneck if verification time exceeds user expectations.

**Mitigation if needed**: Use FAISS `IndexFlatL2` for the per-candidate correspondence search instead of manual loops.

### 8. SHOT NaN Rates Unknown 🟡

SHOT produces NaN descriptors for degenerate points (insufficient neighbors within `shotRadius`). These are filtered out, but the NaN rate is unknown for real dental crown data. If a large fraction of keypoints produce NaN:
- Fewer features for voting → reduced recall
- Fewer correspondences for RANSAC/ICP → lower confidence

**Diagnostic**: Log the NaN filter rate during extraction.

### 9. Composite ID Limit 🟢

FAISS composite IDs encode `unitId × 10000 + keypointIdx`. This limits each unit to 10,000 keypoints (more than sufficient — ISS typically finds 500-1000) and unit IDs to `INT64_MAX / 10000 ≈ 922 trillion` (effectively unlimited).

### 10. Old Data Files May Exist 🟢

After the pipeline migration, the following files from the old FPFH+Codebook system may still exist and should be deleted:
- `aum.db` — contains FPFH descriptors in the old blob format (no SH01 magic)
- `codebook.bin` — k-means codebook (16.9 KB, completely unused)
- Any old FAISS index files (`.faiss`)

These files will cause deserialization errors if the new system tries to read them.

---

## Resolved Issues (Previously Active)

### ~~Codebook Provenance Unknown~~ ✅ RESOLVED
The codebook has been completely deleted from the codebase. No codebook is used in the new ISS+SHOT352 pipeline.

### ~~FAISS IndexFlatIP is O(n)~~ ✅ RESOLVED
Replaced with `IndexIVFFlat` which provides sub-linear search: O(n/nlist × nprobe). With 20K units, queries touch ~1/8 of the database.

### ~~Point-to-Point KD-Tree Rebuilds Per Candidate~~ ✅ RESOLVED
nanoflann KD-tree comparison has been replaced with RANSAC+ICP geometric verification, which uses PCL's built-in solvers.

### ~~Float Precision in Confidence Mapping~~ ✅ RESOLVED
The old inner-product-to-percentage mapping had precision issues. The new scoring formula (`0.4 × ransacInlierRatio + 0.6 × exp(-icpFitness / 0.5)`) is numerically stable.
