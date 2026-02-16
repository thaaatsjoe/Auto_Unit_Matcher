# Brain 3: Data Flow

## Registration Flow

When a new STL file is detected by `StlMonitorService`, the system registers it as a new unit.

```
StlMonitorService detects new STL
  │
  ▼
UnitService.RegisterAsync(stlPath)
  │
  ▼ Parse STL → PointCloud<PointXYZ>
  │   aum_parse_stl(path, &pcHandle)
  │
  ▼ Extract ISS+SHOT352 Descriptor
  │   aum_extract_descriptors(pcHandle, &descHandle)
  │   
  │   Internal pipeline:
  │   1. Downsample (VoxelGrid 0.5mm)
  │   2. Estimate normals (NormalEstimationOMP, radius=2mm)
  │   3. Detect ISS keypoints (salient=3mm, nonMax=2mm)
  │   4. Compute SHOT352 features (radius=5mm)
  │   5. Filter NaN descriptors
  │
  ▼ Serialize descriptor to SH01 blob
  │   aum_serialize_descriptor(descHandle, &blob, &len)
  │   Format: [SH01 magic][count][XYZ positions][SHOT352 data]
  │
  ▼ Store in SQLite
  │   INSERT INTO units (case_id, stl_path, descriptor_blob, ...)
  │
  ▼ Add keypoints to FAISS index
  │   FingerprintEngine.AddToIndex(unitId, descriptorBlob)
  │     → aum_deserialize_descriptor(blob, len, &descHandle)
  │     → aum_add_to_index(indexHandle, descHandle, unitId)
  │     → Encodes composite IDs: unitId * 10000 + keypointIdx
  │     → Buffers until trainIndex() or adds directly if trained
  │
  ▼ Free handles
      aum_free_descriptor(descHandle)
      aum_free_point_cloud(pcHandle)
```

## Index Training Flow

After all units are loaded, the index must be trained before querying:

```
IndexService.InitializeAsync()
  │
  ▼ Load all units from SQLite
  │   var units = unitRepository.GetAllAsync()
  │
  ▼ For each unit:
  │   FingerprintEngine.AddToIndex(unit.Id, unit.DescriptorBlob)
  │
  ▼ Train the IVF index
  │   FingerprintEngine.TrainIndex()
  │     → aum_train_index(indexHandle)
  │     → nlist = sqrt(n), clamped [1, 4096]
  │     → Creates IndexIVFFlat (quantizer: IndexFlatL2)
  │     → Trains on all buffered vectors
  │     → Adds all vectors with composite IDs
  │     → Sets nprobe = min(nlist, 32)
  │     → Clears pending buffers
  │
  ▼ Index is ready for queries
```

## Matching Flow

When an operator scans a unit for identification:

```
MatchingService.FindMatchesFromStlAsync(stlPath, topK: 5)
  │
  ▼ Extract ISS+SHOT352 Descriptor
  │   FingerprintEngine.ExtractDescriptor(stlPath)
  │   → aum_parse_stl → aum_extract_descriptors → aum_serialize_descriptor
  │   Returns: byte[] descriptorBlob (SH01 format)
  │
  ▼ Stage 1: FAISS Voting (~100ms)
  │   FingerprintEngine.QueryVotes(descriptorBlob, topK: 10)
  │     → aum_deserialize_descriptor(blob) → queryDescHandle
  │     → aum_query_votes(indexHandle, queryDescHandle, 10, results, &count)
  │
  │   Internal:
  │   1. Extract all SHOT352 vectors from query descriptor
  │   2. Batch search FAISS: each keypoint → top 3 nearest neighbors (L2)
  │   3. Decode composite IDs → unit IDs
  │   4. Accumulate weighted votes: weight = 1/(1+distance)
  │   5. Sort by total vote score descending
  │   6. Return top 10 VoteResult { unitId, voteScore, voteCount }
  │
  ▼ Stage 2: RANSAC + ICP Verification (per candidate)
  │   For each vote candidate:
  │     Unit = await unitRepository.GetByIdAsync(vote.UnitId)
  │     FingerprintEngine.Verify(descriptorBlob, unit.DescriptorBlob)
  │       → aum_verify(queryDescHandle, candidateDescHandle, &result)
  │
  │     Internal:
  │     1. Build SHOT correspondences (brute-force L2 in 352-D)
  │     2. RANSAC: filter outliers (threshold=1mm, 1000 iterations)
  │     3. ICP: refine alignment using RANSAC transform as seed
  │        (maxCorrespondenceDist=2mm, maxIter=50)
  │     4. Compute final score:
  │        icpQuality = exp(-icpFitness / 0.5)
  │        finalScore = 100 × (0.4 × ransacInlierRatio + 0.6 × icpQuality)
  │
  ▼ Rank by finalScore descending, take top 5
  │   Return MatchingResult[] { UnitId, CaseId, StlPath, Confidence, Rank }
```

## Descriptor Blob Format — SH01

The binary blob stored in SQLite and exchanged between C++ and C# uses a custom format:

```
Offset  Size                Content
------  ------------------  ---------------------------
0       4 bytes             Magic: "SH01" (0x53 0x48 0x30 0x31)
4       4 bytes (uint32)    Keypoint count (N)
8       N × 12 bytes        Keypoint XYZ positions (3 × float32 each)
8+N×12  N × 1408 bytes      SHOT352 descriptors (352 × float32 each)
```

### Size Estimates

| Keypoints | XYZ bytes | SHOT bytes | Total |
|-----------|-----------|------------|-------|
| 500 | 6,000 | 704,000 | ~694 KB |
| 700 | 8,400 | 985,600 | ~971 KB |
| 1000 | 12,000 | 1,408,000 | ~1.38 MB |

Typical dental crown: 600–800 keypoints → **~900 KB – 1.1 MB per unit**

At scale: 20,000 units × 1 MB = ~20 GB in the `descriptor_blob` column

## Handle Lifecycle

The C API uses opaque handle types for cross-boundary resource management:

| Handle | Created by | Freed by | Wraps |
|--------|-----------|----------|-------|
| `AUM_PointCloudHandle` | `aum_parse_stl` | `aum_free_point_cloud` | `pcl::PointCloud<PointXYZ>::Ptr` |
| `AUM_DescriptorHandle` | `aum_extract_descriptors` or `aum_deserialize_descriptor` | `aum_free_descriptor` | `aum::Descriptor` |
| `AUM_IndexHandle` | `aum_create_index` or `aum_load_index` | `aum_free_index` | `aum::MatchingIndex` |

C# uses `SafeHandle` wrappers (`PointCloudHandle`, `DescriptorHandle`, `IndexHandle`) that call the corresponding free functions in their `ReleaseHandle()` override.

## C API Function Inventory

| Function | Category |
|----------|----------|
| `aum_parse_stl` | STL Parsing |
| `aum_point_cloud_size` | STL Parsing |
| `aum_free_point_cloud` | STL Parsing |
| `aum_extract_descriptors` | Descriptor |
| `aum_serialize_descriptor` | Descriptor |
| `aum_deserialize_descriptor` | Descriptor |
| `aum_descriptor_size` | Descriptor |
| `aum_free_descriptor` | Descriptor |
| `aum_free_blob` | Descriptor |
| `aum_create_index` | Index |
| `aum_add_to_index` | Index |
| `aum_train_index` | Index |
| `aum_query_votes` | Index |
| `aum_verify` | Index |
| `aum_save_index` | Index |
| `aum_load_index` | Index |
| `aum_index_size` | Index |
| `aum_index_trained` | Index |
| `aum_free_index` | Index |
| `aum_get_last_error` | Utility |
| `aum_get_version` | Utility |

**Total: 21 C API functions**
