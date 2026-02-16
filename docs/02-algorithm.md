# Brain 2: Matching Algorithm

## Pipeline Overview

AUM uses a **two-stage matching pipeline** based on local 3D feature voting with geometric verification:

1. **Stage 1 — FAISS Voting** (~100ms): Each ISS keypoint's SHOT352 descriptor votes for the most similar registered units via IndexIVFFlat. Returns top 10 voted candidates.
2. **Stage 2 — RANSAC + ICP Verification** (~200ms per candidate): Geometric alignment verification. RANSAC filters spurious correspondences, ICP refines surface alignment, fitness score = final confidence.

```
STL File
  │
  ▼
┌─────────────────────┐
│  STL Parser          │  Binary/ASCII auto-detect
│  stl_parser.cpp      │  → PointCloud<PointXYZ>
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│  Downsample          │  VoxelGrid (0.5mm leaf)
│  descriptor.cpp      │  → Reduced cloud
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│  Normal Estimation   │  NormalEstimationOMP
│  descriptor.cpp      │  radius = 2.0mm
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│  ISS Keypoints       │  ISSKeypoint3D
│  descriptor.cpp      │  500-1000 keypoints
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│  SHOT352 Features    │  SHOTEstimationOMP
│  descriptor.cpp      │  352-dim per keypoint
└──────────┬──────────┘
           │
     ┌─────┴──────┐
     ▼             ▼
┌──────────┐  ┌──────────────────────┐
│ Stage 1  │  │ Stage 2              │
│ FAISS    │  │ RANSAC + ICP         │
│ IVFFlat  │  │ Correspondence match │
│ L2 vote  │  │ Outlier rejection    │
│ Top 10   │  │ Surface alignment    │
│          │  │ Final score 0-100%   │
└──────────┘  └──────────────────────┘
```

---

## Step 1: STL Parsing

**Source**: `stl_parser.cpp`, class `STLParser`

### Binary Detection (`isBinarySTL`)
1. Read 80-byte header
2. Read 4-byte triangle count (`uint32`)
3. Calculate expected file size: `80 + 4 + triangleCount × 50`
4. If actual file size matches → binary; otherwise → ASCII

### Binary Format
```
[80 bytes header]
[4 bytes uint32 triangle_count]
For each triangle (50 bytes):
  [12 bytes normal: 3 × float]   ← ignored, normals recomputed
  [12 bytes v1: 3 × float]
  [12 bytes v2: 3 × float]
  [12 bytes v3: 3 × float]
  [2 bytes attribute: uint16]    ← unused
```

Each triangle contributes 3 points. A typical dental crown STL has ~50K–200K triangles → 150K–600K points before downsampling.

### ASCII Format
Parses `vertex x y z` lines between `solid`/`endsolid` markers.

---

## Step 2: Downsampling

**Source**: `descriptor.cpp`, `DescriptorExtractor::downsample()`

VoxelGrid filter reduces the raw point cloud for efficient processing:

- **Leaf size**: 0.5mm (configured in `DescriptorConfig`)
- **Skip threshold**: Clouds ≤ 2000 points are not downsampled
- **Adaptive scaling**: If still > 20,000 points after initial downsample, voxel size increases by 1.5× iteratively (capped at 10mm)

---

## Step 3: ISS Keypoint Detection

**Source**: `descriptor.cpp`, `DescriptorExtractor::detectKeypoints()`

**Intrinsic Shape Signatures** (ISS) detects geometrically interesting points — cusps, pits, fissures, and margin lines on dental crowns. Unlike random downsampling, ISS selects points that are structurally distinctive and therefore more informative for matching.

### How ISS Works

ISS analyzes the eigenvalues of the local surface scatter matrix at each point:
1. Build a neighborhood from the salient radius (3.0mm)
2. Compute the weighted covariance matrix of neighbors
3. Compute eigenvalues λ₁ ≥ λ₂ ≥ λ₃
4. A point is a keypoint if:
   - λ₂/λ₁ < threshold21 (0.975) — not too plate-like
   - λ₃/λ₂ < threshold32 (0.975) — not too rod-like
   - Has enough neighbors (≥ 5)
5. Non-maximum suppression (radius 2.0mm) removes nearby duplicates

### Keypoint Count

ISS typically detects 500–1000 keypoints on a dental crown. If more than `maxKeypoints` (1000) are detected, uniform subsampling reduces the count.

### Configuration (`DescriptorConfig` in `descriptor.h`)

| Parameter | Default | Unit | Purpose |
|-----------|---------|------|---------| 
| `voxelSize` | **0.5** | mm | VoxelGrid leaf size for downsampling |
| `normalRadius` | **2.0** | mm | Radius for normal estimation (KdTree) |
| `issSalientRadius` | **3.0** | mm | ISS neighborhood radius for eigenvalue computation |
| `issNonMaxRadius` | **2.0** | mm | Non-maximum suppression radius |
| `issThreshold21` | **0.975** | ratio | λ₂/λ₁ eigenvalue ratio threshold |
| `issThreshold32` | **0.975** | ratio | λ₃/λ₂ eigenvalue ratio threshold |
| `issMinNeighbors` | **5** | count | Minimum neighbors for valid keypoint |
| `maxKeypoints` | **1000** | count | Cap on keypoints (subsampled if exceeded) |
| `shotRadius` | **5.0** | mm | SHOT descriptor search radius |

---

## Step 4: SHOT352 Descriptor Extraction

**Source**: `descriptor.cpp`, `DescriptorExtractor::extract()`

**Signature of Histograms of Orientations** (SHOT352) computes a 352-dimensional descriptor at each ISS keypoint, encoding local surface geometry within a spherical neighborhood.

### How SHOT Works

1. Create a local reference frame (LRF) at each keypoint using eigenvalue decomposition
2. Divide the spherical neighborhood into 32 spatial volumes (8 azimuth × 2 elevation × 2 radial divisions)
3. In each volume, compute an 11-bin histogram of angles between surface normals and the local z-axis
4. Concatenate all histograms: 32 × 11 = **352** floats
5. L2-normalize the final descriptor

### Why SHOT352 Over FPFH

| Property | FPFH (33D) — old | SHOT352 (352D) — new |
|---|---|---|
| Dimensionality | 33 | 352 |
| Spatial encoding | None (global feature distribution) | 32 local volumes (encodes WHERE geometry is) |
| Rotation invariance | Yes (angle-based) | Yes (local reference frame) |
| Partial scan robustness | Poor (histogram distortion from missing geometry) | Strong (spatial volumes unaffected by geometry in other volumes) |
| Per-keypoint utility | Low (all points averaged the same way) | High (each keypoint is independently distinctive) |

### NaN Filtering

SHOT produces NaN descriptors for degenerate points (insufficient neighbors, numerical instability). All NaN descriptors are filtered out alongside their keypoints before storing.

### Computation

```
Raw PointCloud (150K-600K points)
  │
  ▼ Downsample VoxelGrid (0.5mm)
  │  Adaptive if > 20,000 points: leaf × 1.5 iteratively
  │
  ▼ Downsampled Cloud
  │
  ▼ Normal Estimation (NormalEstimationOMP, radius=2.0mm)
  │
  ▼ ISS Keypoint Detection (salient=3.0mm, nonMax=2.0mm)
  │  → 500-1000 keypoints (subsampled if > maxKeypoints)
  │
  ▼ SHOT352 Estimation (SHOTEstimationOMP, radius=5.0mm)
  │  Input: keypoints. Surface: downsampled cloud. Normals: from above.
  │
  ▼ NaN Filter (remove degenerate SHOT descriptors)
  │
  ▼ Descriptor Object (keypoints XYZ + SHOT352 features)
```

---

## Step 5: FAISS Index — Local Feature Voting (Stage 1)

**Source**: `matching.cpp`, class `MatchingIndex`

### Index Type

`faiss::IndexIVFFlat` (Inverted File Index with flat quantizer)

- **Quantizer**: `IndexFlatL2` — exact L2 distance for cluster assignment
- **Metric**: `METRIC_L2` — L2 (Euclidean) distance in 352-D SHOT space
- **nlist**: `sqrt(n)` clusters, clamped to [1, 4096] (requires at least `nlist × 39` vectors to train)
- **nprobe**: `min(nlist, 32)` — searches up to 32 clusters per query for recall

### ID Encoding — Composite IDs

Each FAISS vector represents a single keypoint, not a whole unit. To track which unit a keypoint belongs to:

```
faiss_id = unitId * 10000 + keypointIndex
unitId   = faiss_id / 10000
kpIdx    = faiss_id % 10000
```

Supports up to 10,000 keypoints per unit and ~922 trillion unit IDs.

### Adding to Index

```
Descriptor → for each keypoint:
    Extract SHOT352 vector (352 floats)
    Encode composite ID = unitId * 10000 + keypointIdx
    → buffer in pendingVectors_ / pendingIds_
```

After all units are added: `trainIndex()` builds the IVF structure and inserts all buffered vectors. Subsequent `add()` calls insert directly into the trained index.

### Voting Query

For each scan:
1. Extract all query keypoint SHOT352 vectors
2. Batch search FAISS: each query keypoint → top 3 nearest neighbors (352-D L2 distance)
3. Decode composite IDs → extract unit IDs
4. Accumulate weighted votes per unit: `weight = 1 / (1 + distance)` — closer matches get stronger votes
5. Sort units by total vote score descending
6. Return top 10 units

### Why Voting Works for Partial Scans

A partial scan has fewer keypoints but the keypoints it does have still individually match their counterparts in the database. Even with 50% of the crown missing, the remaining keypoints still vote for the correct unit. This is fundamentally different from the old histogram approach where missing geometry distorted the entire representation.

### Performance Characteristics

- `IndexIVFFlat` is **O(n/nlist × nprobe)** — sub-linear search
- With nlist=256, nprobe=32: queries search ~1/8 of the database
- At 20,000 units × 700 keypoints = ~14M vectors: query time ≈ 100ms

---

## Step 6: RANSAC + ICP Geometric Verification (Stage 2)

**Source**: `matching.cpp`, `MatchingIndex::verify()`

### Step 6a: SHOT Correspondence Building

For each query keypoint, find its nearest neighbor in the candidate's SHOT feature space (brute-force L2 over 352 dimensions). This produces a set of `pcl::Correspondence` objects linking query keypoints to candidate keypoints.

### Step 6b: RANSAC Outlier Rejection

**Random Sample Consensus** (RANSAC) filters spurious correspondences:

1. Randomly sample 3 correspondences
2. Estimate a rigid transform (rotation + translation)
3. Count inliers: correspondences consistent with the transform within `ransacThreshold` (default 1.0mm)
4. Repeat up to **1000 iterations**, keep the best transform
5. Return only the inlier correspondences

### Step 6c: ICP Surface Alignment

**Iterative Closest Point** (ICP) refines the RANSAC transform for precise alignment:

| Parameter | Value | Purpose |
|-----------|-------|---------|
| Max correspondence distance | **2.0mm** | Points farther apart are not correspondences |
| Max iterations | **50** | Convergence limit |
| Transformation epsilon | **1e-8** | Stop when transform change is negligible |
| Euclidean fitness epsilon | **1e-6** | Stop when fitness improvement is negligible |

ICP outputs:
- **Fitness score**: Mean squared distance of all inlier correspondences. **Lower = better**. 0.0 = perfect alignment.

### Step 6d: Final Confidence Score

```
icpQuality = exp(-icpFitnessScore / 0.5)

finalScore = 100 × (0.4 × ransacInlierRatio + 0.6 × icpQuality)
```

- **40%** from RANSAC inlier ratio — did the features match spatially?
- **60%** from ICP quality — did the surfaces actually align?
- Clamped to [0, 100]

| ICP Fitness | icpQuality | Meaning |
|-------------|------------|---------|
| 0.0 | 1.00 | Perfect surface alignment |
| 0.1 | 0.82 | Excellent alignment |
| 0.5 | 0.37 | Moderate alignment |
| 1.0 | 0.14 | Poor alignment |
| 2.0 | 0.02 | Very poor — likely wrong match |

---

## Matching Result Flow (C# MatchingService)

**Source**: `MatchingService.cs`

```csharp
// Stage 1: FAISS voting
var voteResults = _engine.QueryVotes(descriptor, voteCandidates: 10);

// Stage 2: Geometric verification for each candidate
foreach (var vote in voteResults) {
    var unit = await _unitRepository.GetByIdAsync(vote.UnitId);
    var verification = _engine.Verify(descriptor, unit.DescriptorBlob);
    candidates.Add(new { unit, confidence: verification.FinalScore });
}

// Final: top-K by verified score
return candidates.OrderByDescending(r => r.Confidence).Take(topK: 5);
```

The final confidence displayed to the operator comes from **Stage 2 only** (RANSAC + ICP). Stage 1 voting is purely for fast candidate retrieval.

If verification fails for a candidate, vote score is used as a fallback: `finalScore = min(100, voteScore × 10)`.

---

## Key Parameters Summary

| Constant | Value | Location | Purpose |
|----------|-------|----------|---------|
| SHOT dimension | 352 | `Descriptor::SHOT_DIM` | Fixed by the SHOT352 algorithm |
| Voxel size | 0.5 mm | `descriptor.h` | Downsampling resolution |
| Normal radius | 2.0 mm | `descriptor.h` | Normal estimation neighborhood |
| ISS salient radius | 3.0 mm | `descriptor.h` | Keypoint saliency neighborhood |
| ISS non-max radius | 2.0 mm | `descriptor.h` | Non-maximum suppression |
| ISS threshold21 | 0.975 | `descriptor.h` | Eigenvalue ratio gate |
| ISS threshold32 | 0.975 | `descriptor.h` | Eigenvalue ratio gate |
| ISS min neighbors | 5 | `descriptor.h` | Minimum neighborhood size |
| Max keypoints | 1000 | `descriptor.h` | Cap after ISS detection |
| SHOT radius | 5.0 mm | `descriptor.h` | SHOT feature radius |
| Vote candidates | 10 | `MatchingService.cs` | Stage 1 shortlist size |
| Neighbors per keypoint | 3 | `matching.h:99` | FAISS search breadth |
| RANSAC threshold | 1.0 mm | `matching.h:113` | Inlier distance threshold |
| RANSAC iterations | 1000 | `matching.cpp:302` | Max RANSAC iterations |
| ICP max correspondence | 2.0 mm | `matching.h:114` | Max point pair distance |
| ICP max iterations | 50 | `matching.cpp:331` | ICP convergence limit |
| Fitness tolerance | 0.5 mm² | `matching.cpp:347` | ICP quality decay denominator |
| RANSAC weight | 40% | `matching.cpp:353` | Score contribution from RANSAC |
| ICP weight | 60% | `matching.cpp:353` | Score contribution from ICP |
| Top-K results | 5 | `IMatchingService.cs` | Final results shown to operator |
