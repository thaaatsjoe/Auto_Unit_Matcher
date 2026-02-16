# Brain 1: Architecture

## Three-Layer Stack

```
┌─────────────────────────────┐
│  AUM.UI  (WPF .NET 8)      │  XAML views, ViewModels, Serilog
│  src/AUM.UI/                │  Login → MainWindow → 3D Viewer + Results
├─────────────────────────────┤
│  AUM.Core  (C# class lib)  │  Services, Repositories, Engine wrapper
│  src/AUM.Core/              │  DI container, SQLite, NativeMethods P/Invoke
├─────────────────────────────┤
│  AUM.Engine  (C++ DLL)     │  PCL, FAISS, STL parsing
│  src/AUM.Engine/            │  ISS + SHOT352 + IVFFlat + RANSAC + ICP
└─────────────────────────────┘
```

## C++ Engine — Source Files

| Directory | File | Purpose |
|-----------|------|---------|
| `include/` | `exports.h` | C API declarations (21 functions), handle types, error codes, result structs |
| `include/` | `descriptor.h` | `DescriptorConfig`, `Descriptor` (keypoints+SHOT), `DescriptorExtractor` |
| `include/` | `matching.h` | `MatchingIndex`, `VoteResult`, `VerificationResult` |
| `include/` | `stl_parser.h` | `STLParser` (binary/ASCII STL → PCL cloud) |
| `src/` | `engine_api.cpp` | C API implementations (extern "C" wrappers around C++ classes) |
| `src/` | `descriptor.cpp` | ISS keypoint detection + SHOT352 extraction pipeline |
| `src/` | `matching.cpp` | FAISS IndexIVFFlat voting + RANSAC + ICP verification |
| `src/` | `stl_parser.cpp` | STL file parsing with binary/ASCII auto-detection |
| `tests/` | `test_descriptor.cpp` | GTest for descriptor extraction |
| `tests/` | `test_matching.cpp` | GTest for matching index |
| `tests/` | `test_stl_parser.cpp` | GTest for STL parser |

### Dependencies

| Library | vcpkg Package | Purpose |
|---------|--------------|---------|
| PCL | `pcl` | Point cloud processing (keypoints, normals, registration, features) |
| FAISS | `faiss` | Vector similarity search (IndexIVFFlat) |
| SQLite | `sqlite3` | Database operations |

### Build

```
CMake 3.28+ → vcpkg toolchain → MSVC 2022
Build type: Release
Output: AUM.Engine.dll (deployed alongside C# exe)
```

## Interop Pattern — Opaque Handles

C# communicates with C++ through a flat C API (no C++ classes or exceptions cross the boundary).

### Handle Types

| C Handle | C# Wrapper | Wraps |
|----------|-----------|-------|
| `AUM_PointCloudHandle` (void*) | `PointCloudHandle : SafeHandle` | `pcl::PointCloud<PointXYZ>::Ptr` |
| `AUM_DescriptorHandle` (void*) | `DescriptorHandle : SafeHandle` | `aum::Descriptor` |
| `AUM_IndexHandle` (void*) | `IndexHandle : SafeHandle` | `aum::MatchingIndex` |

### Error Handling

- C++ exceptions are caught at the C API boundary in `engine_api.cpp`
- Error code (`AUM_ErrorCode`) is returned from every C function
- Error message stored in thread-local buffer, retrievable via `aum_get_last_error()`
- C# throws `EngineException` if error code ≠ `AUM_SUCCESS`

## C# Core — Namespace Layout

```
AUM.Core
├── Data/
│   ├── DatabaseContext.cs         — SQLite connection + schema init
│   └── Repositories/
│       ├── UnitRepository.cs      — CRUD for units table
│       ├── UserRepository.cs      — Authentication + operator tracking
│       ├── AuditRepository.cs     — Audit log operations
│       ├── ScanHistoryRepository.cs
│       ├── ExtractionQueueRepository.cs
│       ├── AbsCacheRepository.cs
│       └── RemillingFlagRepository.cs
├── DependencyInjection/
│   └── ServiceCollectionExtensions.cs  — AddAumCore() + InitializeAumCoreAsync()
├── Engine/
│   ├── FingerprintEngine.cs       — High-level C++ engine wrapper
│   ├── IFingerprintEngine.cs      — Engine interface
│   └── EngineException.cs         — Exception type for C++ errors
├── Interop/
│   ├── NativeMethods.cs           — P/Invoke declarations
│   ├── PointCloudHandle.cs        — SafeHandle for point clouds
│   ├── DescriptorHandle.cs        — SafeHandle for descriptors
│   └── IndexHandle.cs             — SafeHandle for FAISS index
└── Services/
    ├── MatchingService.cs         — Two-stage matching orchestration
    ├── UnitService.cs             — Registration, deactivation
    ├── IndexService.cs            — Index build + training
    ├── AuditService.cs            — Audit logging
    └── StlMonitorService.cs       — FileSystemWatcher for new STLs
```

## DI Container Registration

**Source**: `ServiceCollectionExtensions.AddAumCore(databasePath)`

| Service | Lifetime | Implementation |
|---------|----------|----------------|
| `DatabaseContext` | Singleton | `DatabaseContext.CreateFromFile(path)` |
| `IUnitRepository` | Scoped | `UnitRepository` |
| `IUserRepository` | Scoped | `UserRepository` |
| `IAuditRepository` | Scoped | `AuditRepository` |
| `IScanHistoryRepository` | Scoped | `ScanHistoryRepository` |
| `IExtractionQueueRepository` | Scoped | `ExtractionQueueRepository` |
| `IAbsCacheRepository` | Scoped | `AbsCacheRepository` |
| `IRemillingFlagRepository` | Scoped | `RemillingFlagRepository` |
| `IFingerprintEngine` | Singleton | `FingerprintEngine` |
| `IIndexService` | Singleton | `IndexService` |
| `IUnitService` | Scoped | `UnitService` |
| `IMatchingService` | Scoped | `MatchingService` |
| `IAuditService` | Scoped | `AuditService` |
| `IStlMonitorService` | Singleton | `StlMonitorService` |

**Note**: `AddAumCore` takes only `databasePath`. No codebook path is needed — SHOT352 keypoints are indexed directly in FAISS.

## Application Lifecycle

```
App.OnStartup()
  │
  ├─ Configure Serilog (file + console sink)
  ├─ ConfigureServices()
  │    └─ services.AddAumCore(databasePath)
  ├─ Build ServiceProvider
  ├─ InitializeAumCoreAsync()
  │    ├─ DatabaseContext.InitializeAsync()  → schema creation
  │    └─ IndexService.InitializeAsync()     → load all units + train index
  │
  ├─ Check IsConfigured()
  │    └─ If first run → ShowFirstRunSetup()
  │
  ├─ ShowLoginWindow() → operator authentication
  │    └─ On success → ShowMainWindow()
  │
  └─ StartStlMonitoringAsync()
       └─ FileSystemWatcher on configured scanner output dir
       └─ OnStlFileDetected → extract, register, match

App.OnExit()
  └─ Dispose ServiceProvider (FingerprintEngine → frees C++ handles)
```
