# Brain 6: Configuration

## C++ Engine — Descriptor Extraction Parameters

Configured in `DescriptorConfig` struct (`descriptor.h`). All radii are in millimeters (1:1 patient scale, no scaling applied).

### Downsampling

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `voxelSize` | 0.5 mm | VoxelGrid leaf size. Smaller = more points, slower. |

- Skip threshold: clouds ≤ 2,000 points are not downsampled
- Adaptive scaling: if > 20,000 points after initial downsample, leaf size increases by 1.5× iteratively (max 10mm)

### Normal Estimation

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `normalRadius` | 2.0 mm | KdTree search radius for `NormalEstimationOMP` |

### ISS Keypoint Detection

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `issSalientRadius` | 3.0 mm | Neighborhood radius for eigenvalue computation |
| `issNonMaxRadius` | 2.0 mm | Non-maximum suppression radius |
| `issThreshold21` | 0.975 | λ₂/λ₁ eigenvalue ratio gate (rejects plate-like surfaces) |
| `issThreshold32` | 0.975 | λ₃/λ₂ eigenvalue ratio gate (rejects rod-like surfaces) |
| `issMinNeighbors` | 5 | Minimum neighbors for valid keypoint |
| `maxKeypoints` | 1000 | Cap on keypoints (uniform subsampling if exceeded) |

### SHOT352 Descriptor

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `shotRadius` | 5.0 mm | Search radius for `SHOTEstimationOMP` |

---

## C++ Engine — Matching Parameters

Configured as defaults in `MatchingIndex` method signatures (`matching.h`).

### FAISS Index (Stage 1)

| Parameter | Computed | Purpose |
|-----------|----------|---------|
| `nlist` | `sqrt(n)`, clamped [1, 4096] | Number of IVF clusters |
| `nprobe` | `min(nlist, 32)` | Clusters searched per query |
| `neighborsPerKeypoint` | 3 | FAISS neighbors returned per query keypoint |
| Metric | L2 (Euclidean) | Distance metric in 352-D SHOT space |

### Voting (Stage 1)

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `topK` (MatchingService) | 10 | Vote candidates passed to Stage 2 |
| Vote weight formula | `1 / (1 + distance)` | Closer matches get stronger votes |

### Geometric Verification (Stage 2)

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `ransacThreshold` | 1.0 mm | RANSAC inlier distance threshold |
| `ransacMaxIterations` | 1000 | Max RANSAC iterations |
| `icpMaxCorrespondenceDist` | 2.0 mm | ICP max correspondence distance |
| `icpMaxIterations` | 50 | ICP convergence limit |
| `icpTransformationEpsilon` | 1e-8 | Stop when transform delta is negligible |
| `icpEuclideanFitnessEpsilon` | 1e-6 | Stop when fitness improvement is negligible |

### Score Computation

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `fitnessTolerance` | 0.5 mm² | ICP quality decay: `exp(-fitness / 0.5)` |
| RANSAC weight | 40% | Score contribution from RANSAC inlier ratio |
| ICP weight | 60% | Score contribution from ICP quality |
| Final results (`topK`) | 5 | MatchingService returns top 5 to UI |

---

## C# Service Configuration

### MatchingService

| Parameter | Value | Location |
|-----------|-------|----------|
| Vote candidates | 10 | `FindMatchesAsync()` |
| Default topK | 5 | `IMatchingService.cs` |
| Vote fallback formula | `min(100, voteScore × 10)` | When Stage 2 fails |

### FingerprintEngine

| Parameter | Value | Purpose |
|-----------|-------|---------|
| Index lock | SemaphoreSlim(1,1) | Thread-safe index access |
| Version | From `aum_get_version()` | C++ DLL version string |

### StlMonitorService

| Parameter | Source | Purpose |
|-----------|-------|---------|
| Watch directory | User settings | Network share path for scanner output |
| Filter | `*.stl` | File extension filter |
| Startup scan | All existing files | Catches files added while app was closed |

---

## Application Paths

| Path | Default / Source | Purpose |
|------|-----------------|---------|
| Database | `aum.db` | SQLite database (units, users, audit, etc.) |
| FAISS index | Not currently persisted | In-memory, rebuilt on startup from database |
| Logs | `logs/` (via Serilog) | Application log files |
| STL watch | User-configured | Scanner output directory (typically network share) |

> **Note**: There is no `codebook.bin` or codebook path. The old codebook was deleted in the pipeline migration to ISS+SHOT352. SHOT352 keypoints are indexed directly in FAISS.

---

## Tuning Guidance

### If ISS detects too few keypoints
- Decrease `issSalientRadius` (try 2.0mm) — smaller neighborhood finds more features
- Increase `issThreshold21` / `issThreshold32` (closer to 1.0) — less restrictive eigenvalue filter
- Decrease `issMinNeighbors` (try 3) — accepts sparser regions

### If ISS detects too many keypoints
- Increase `issSalientRadius` (try 4.0mm) — larger neighborhood, fewer significant points
- Decrease `issThreshold21` / `issThreshold32` (try 0.9) — more restrictive filter
- Decrease `maxKeypoints` (try 500) — hard cap

### If SHOT descriptors are mostly NaN
- Increase `shotRadius` (try 7.0mm) — more surface context per keypoint
- Increase `normalRadius` — better normal estimates
- Check input STL mesh quality — degenerate triangles cause issues

### If Stage 2 gives low confidence on correct matches
- Increase `icpMaxCorrespondenceDist` (try 3.0mm) — more tolerant alignment  
- Decrease `ransacThreshold` (try 0.5mm) — tighter inlier criterion
- Check if partial scan is too small — may not have enough overlap

### If voting returns wrong candidates (Stage 1 miss)
- Increase `neighborsPerKeypoint` (try 5) — better recall, slightly slower
- Increase `nprobe` — search more clusters (at cost of speed)
- Ensure index is trained with sufficient data (minimum ~39 × nlist vectors)
