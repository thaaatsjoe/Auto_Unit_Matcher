# Auto Unit Matcher (AUM) - Product Requirements Document

## Executive Summary

A dental lab unit identification system that automatically matches sintered/polished dental restorations (crowns, bridges) back to their original cases using 3D scanning and shape-matching technology.

**Problem:** High-volume dental labs mill hundreds of units daily that become mixed during sintering and polishing. Manually matching units back to cases is time-consuming, error-prone, and a bottleneck.

**Solution:** A dual-camera structured light scanning station that captures 3D geometry of physical units and matches them against a database of STL fingerprints extracted from 3Shape design files.

---

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              AUM SYSTEM                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌──────────────────┐         ┌──────────────────┐         ┌────────────┐  │
│   │   STL Database   │         │   Matching       │         │   Scan     │  │
│   │   Monitor        │────────▶│   Engine         │◀───────│   Station  │  │
│   │                  │         │                  │         │            │  │
│   │  • Watches for   │         │  • 3D descriptor │         │  • Dual    │  │
│   │    new STL files │         │    extraction    │         │    cameras │  │
│   │  • Extracts 3D   │         │  • Scale-inv.    │         │  • Struct. │  │
│   │    fingerprints  │         │    matching      │         │    light   │  │
│   │  • Stores in DB  │         │  • Confidence    │         │  • Rotate  │  │
│   │                  │         │    scoring       │         │    platform│  │
│   └──────────────────┘         └──────────────────┘         └────────────┘  │
│                                        │                                    │
│                                        ▼                                    │
│                           ┌──────────────────────┐                          │
│                           │   User Interface     │                          │
│                           │                      │                          │
│                           │  • 3D result viewer  │                          │
│                           │  • Match confirm     │                          │
│                           │  • Label printing    │                          │
│                           │  • Audible alerts    │                          │
│                           └──────────────────────┘                          │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Hardware Requirements

### Scanning Station Design

```
SIDE VIEW:
           [Proj A] (45 deg angle down)  [Proj B]
               |                            |
          [Camera A]                   [Camera B]
               \                          /
                \  (slight angle down)   /
                 \                      /
                  \    [Crown]         /
                   \      |           /
                    \  [Platform]    /
                         (rotates)

TOP VIEW:
              [Proj A]
              [Camera A]
                  |
                  ↓
              [Crown]  ← lying on its side, held by dental putty
                  ↑
                  |
              [Camera B]
              [Proj B]
              
        ↻ Platform rotates ~20°
```

#### Component Specifications

| Component | Specification | Notes |
|-----------|--------------|-------|
| **Projectors** | DLP structured light, positioned directly above cameras | Alternating pattern projection |
| **Cameras** | Industrial machine vision, 180° apart | Slightly higher than crown, angled down ~5-10° |
| **Platform** | Motorized rotating stage | Fast rotation (~20°), encoder for position tracking |
| **Mounting** | Dental-grade sticky putty | No residue, secure hold during rotation |
| **Enclosure** | Compact benchtop box | Light-blocking curtain/lid, matte black interior |
| **Trigger** | Physical tactile button | USB-connected to PC |
| **Label Printer** | Thermal label printer | Compatible with case information output |

#### Recommended Hardware (Off-the-Shelf)

> [!IMPORTANT]
> Final hardware selection requires vendor evaluation. These are starting points for investigation:

**Structured Light Options:**
- Shining3D EinScan series (dental-focused models)
- Medit T-series (if 3Shape integration is acceptable)
- Photoneo PhoXi 3D Scanner (industrial, open API)
- Custom: DLP projector module (e.g., Texas Instruments DLP) + industrial cameras (e.g., FLIR Blackfly)

**Rotation Stage:**
- Thorlabs motorized rotation stage
- Newport rotation stages
- Or integrated turntable from scanner vendor

**Trigger Button:**
- USB foot pedal or industrial pushbutton with USB HID interface

---

## Software Architecture

### Design Principles

> [!IMPORTANT]
> The codebase must be **object-oriented** and **highly modular** to support maintainability, testing, and future expansion.

| Principle | Implementation |
|-----------|----------------|
| **Object-Oriented** | Clear class hierarchies, encapsulation, interfaces for abstraction |
| **Modular** | Each component (scanner, matcher, UI, printer) is a separate module with defined interfaces |
| **Dependency Injection** | Components receive dependencies via constructor, enabling unit testing |
| **Interface-Driven** | Core services defined by interfaces (e.g., `IScannerDriver`, `IMatchingEngine`, `IABSService`) |
| **Single Responsibility** | Each class has one clear purpose |
| **Loose Coupling** | Modules communicate through well-defined APIs, not internal implementation details |

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AUM Application                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │  STL Monitor    │  │  Scanner Driver │  │  Matching Engine    │  │
│  │  Service        │  │                 │  │                     │  │
│  ├─────────────────┤  ├─────────────────┤  ├─────────────────────┤  │
│  │ • File watcher  │  │ • Camera control│  │ • Descriptor calc   │  │
│  │ • STL parser    │  │ • Projector ctrl│  │ • Database query    │  │
│  │ • Descriptor    │  │ • Platform ctrl │  │ • Confidence score  │  │
│  │   extraction    │  │ • Point cloud   │  │ • Result ranking    │  │
│  │ • Database write│  │   generation    │  │                     │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────┬──────────┘  │
│           │                    │                      │             │
│           └────────────────────┼──────────────────────┘             │
│                                │                                    │
│                                ▼                                    │
│                    ┌───────────────────────┐                        │
│                    │   Fingerprint         │                        │
│                    │   Database            │                        │
│                    │   (SQLite + files)    │                        │
│                    └───────────────────────┘                        │
│                                │                                    │
│           ┌────────────────────┼────────────────────┐               │
│           │                    │                    │               │
│           ▼                    ▼                    ▼               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │  User Interface │  │  Audio Manager  │  │  Label Printer      │  │
│  │                 │  │                 │  │  Driver             │  │
│  ├─────────────────┤  ├─────────────────┤  ├─────────────────────┤  │
│  │ • 3D viewer     │  │ • Match found   │  │ • Case info format  │  │
│  │ • Result display│  │   sound         │  │ • Print queue       │  │
│  │ • Confirmation  │  │ • No match      │  │                     │  │
│  │   controls      │  │   sound         │  │                     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Technology Stack

```
┌─────────────────────────────────────────────────────────────────┐
│                     RECOMMENDED STACK                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  USER INTERFACE: C# with WPF or WinUI 3                  │   │
│  │  • Native Windows look and feel                          │   │
│  │  • 3D viewer via HelixToolkit                            │   │
│  │  • Easy to develop, good performance                     │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                  │
│                              ▼                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  AI ENGINE: Deep GeoTransformer (AUM V2)                 │   │
│  │  • 10.3M Parameter PyTorch Neural Network                │   │
│  │  • Flash Attention for O(N) memory efficiency            │   │
│  │  • WSL2 Ubuntu + Native SSD training pipeline            │   │
│  │  • Point Cloud Library (PCL) & FAISS                     │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                  │
│                              ▼                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  DATABASE: SQLite (local) or PostgreSQL (networked)      │   │
│  │  • Fingerprint storage                                   │   │
│  │  • Cached ABS data                                       │   │
│  │  • Scan history                                          │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Development Environment Setup

#### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| **Visual Studio 2022** | 17.x+ | IDE with C++ and .NET workloads |
| **vcpkg** | Latest | C++ package manager |
| **CMake** | 3.20+ | C++ build system (for engine DLL) |
| **.NET SDK** | 8.0+ | C# development |
| **Git** | Latest | Version control |

#### One-Time Setup

```powershell
# 1. Install vcpkg (run once)
git clone https://github.com/microsoft/vcpkg.git C:\vcpkg
cd C:\vcpkg
.\bootstrap-vcpkg.bat

# 2. Integrate vcpkg with Visual Studio (run once)
.\vcpkg integrate install

# 3. Install C++ dependencies
.\vcpkg install pcl:x64-windows          # Point Cloud Library
.\vcpkg install faiss:x64-windows        # Similarity search
.\vcpkg install sqlite3:x64-windows      # Database
```

#### Build System Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                        BUILD SYSTEM                               │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  C# PROJECTS (WPF UI, Core Logic)                          │  │
│  │  • Managed by: Visual Studio / MSBuild                     │  │
│  │  • Packages: NuGet (HelixToolkit, SQLite, etc.)            │  │
│  │  • Output: .exe / .dll                                     │  │
│  └────────────────────────────────────────────────────────────┘  │
│                              │                                    │
│                         P/Invoke                                  │
│                              ▼                                    │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  C++ ENGINE (Native DLL)                                   │  │
│  │  • Managed by: CMake                                       │  │
│  │  • Packages: vcpkg (PCL, FAISS)                            │  │
│  │  • Output: AUM.Engine.dll                                  │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

#### Why CMake for C++ Engine?

| Reason | Benefit |
|--------|---------|
| **PCL requires it** | Point Cloud Library uses CMake natively |
| **vcpkg integration** | Seamless toolchain file integration |
| **FAISS support** | FAISS also uses CMake |
| **Reproducible builds** | `CMakeLists.txt` + `vcpkg.json` = consistent builds |
| **IDE agnostic** | Generates VS projects, still get full VS experience |

#### Project Structure

```
Auto_Unit_Matcher/
├── PRD.md
├── AUM.sln                          # Visual Studio solution
├── src/
│   ├── AUM.UI/                      # C# WPF project
│   │   ├── AUM.UI.csproj
│   │   └── ...
│   ├── AUM.Core/                    # C# core logic (wraps C++ engine)
│   │   ├── AUM.Core.csproj
│   │   └── ...
│   ├── AUM.Engine/                  # Legacy C++ native DLL
│   │   ├── CMakeLists.txt           # CMake build config
│   │   ├── vcpkg.json               # C++ dependency manifest
│   │   └── src/
│   │       ├── descriptor.cpp       # V1 FPFH computation
│   │       ├── matching.cpp         # FAISS integration
│   │       └── exports.h            # C API for P/Invoke
│   └── tools/
│       └── v2_training/             # AUM V2 Deep Learning Pipeline
│           ├── model.py             # 10.3M Param GeoTransformer
│           ├── train.py             # PyTorch training loop
│           └── preprocess.py        # Voxel Downsampling
├── tests/
│   ├── AUM.Tests/                   # C# unit tests
│   └── AUM.Engine.Tests/            # C++ tests
└── docs/
    └── ...
```

#### NuGet Packages (C#)

| Package | Purpose |
|---------|---------|
| `HelixToolkit.Wpf` | 3D visualization in WPF |
| `Microsoft.Data.Sqlite` | SQLite database access |
| `Serilog` | Logging |
| `NAudio` | Audio playback for alerts |
| `System.Text.Json` | Configuration parsing |

#### Testing Strategy

| Layer | Framework | Mocking | Purpose |
|-------|-----------|---------|---------|
| **C# Unit Tests** | xUnit | Moq | Test UI logic, services, database access |
| **C++ Unit Tests** | Google Test | Google Mock | Test descriptor computation, matching algorithms |
| **Integration Tests** | xUnit | Real components | End-to-end workflow validation |

**Why xUnit (C#):**
- Modern, clean syntax with `[Fact]` and `[Theory]` attributes
- Excellent parallel test execution
- Used by Microsoft for .NET itself
- Great Visual Studio Test Explorer integration

**Why Google Test (C++):**
- Industry standard, integrates with vcpkg: `vcpkg install gtest:x64-windows`
- Native CMake support via `find_package(GTest)`
- Includes Google Mock for mocking

**Test Coverage Goals:**
- Core matching logic: 90%+ coverage
- UI view models: 80%+ coverage
- Integration paths: Key workflows covered

### Configuration

```yaml
# Example configuration structure
database:
  stl_root_path: "D:/3Shape/Output"  # CONFIGURABLE - root of STL database
  fingerprint_db: "./data/fingerprints.db"
  
abs_integration:
  api_endpoint: "http://abs-server/api"  # CONFIGURABLE
  api_key: "YOUR_API_KEY"
  cache_ttl_hours: 24  # refresh cached data after 24 hours
  enabled: false  # PLACEHOLDER - enable when API documentation is available
  
scanner:
  scanner_1_id: "scanner_01"
  scanner_2_id: "scanner_02"  # supports 2 scanners per station
  rotation_degrees: 20
  capture_frames: 10
  
matching:
  algorithm: "FPFH"
  scale_invariant: true
  top_matches: 5  # show top 5 candidates
  
ui:
  enable_3d_viewer: true
  require_confirmation: true
  
audio:
  match_found: "./sounds/match_success.wav"
  no_match: "./sounds/no_match.wav"
  
printer:
  enabled: true
  device: "DYMO LabelWriter 450"
```

---

## ABS Lab Management Integration

> [!WARNING]
> **PLACEHOLDER IMPLEMENTATION**: ABS integration functions will be implemented as stubs/placeholders until ABS API documentation is provided. The application will continue to function without ABS data, using folder-derived case information only.

### Hybrid Caching Strategy

The ABS API may be slow, so we use a hybrid approach to avoid operator wait times:

```
┌─────────────────────────────────────────────────────────────────────┐
│  ABS Data Flow                                                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  BACKGROUND (during STL registration):                               │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐            │
│  │ New STL     │────▶│ Call ABS    │────▶│ Cache data  │            │
│  │ detected    │     │ API         │     │ locally     │            │
│  └─────────────┘     └─────────────┘     └─────────────┘            │
│        │                   │                                         │
│        │              (if slow/unavailable)                          │
│        │                   │                                         │
│        │                   ▼                                         │
│        │            Mark as "pending", retry later                   │
│                                                                      │
│  AT MATCH TIME:                                                      │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐            │
│  │ Match found │────▶│ Check local │───▶│ Use cached  │ (fast!)    │
│  │             │     │ cache       │     │ data        │            │
│  └─────────────┘     └──────┬──────┘     └─────────────┘            │
│                             │                                        │
│                        (if stale >24h or missing)                    │
│                             │                                        │
│                             ▼                                        │
│                      Call ABS API as fallback                        │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Data Retrieved from ABS

| Field | Source | Used For |
|-------|--------|----------|
| Case number | Folder name / ABS | Label, display |
| Due date | ABS API (placeholder) | Label |
| Product name | ABS API (placeholder) | Label |
| Patient name | ABS API (placeholder) | Label |
| Dr Name | ABS API (placeholder) | Label |
| Material | Folder name / ABS | Label, display |
| Tooth info | ABS API (placeholder) | Label |
| RX notes | ABS API (placeholder) | Label |
| Shipping method | ABS API (placeholder) | Label |

### Placeholder Implementation Strategy

Until ABS API documentation is provided:

1. **ABS service interface** will be defined with all required methods
2. **Stub implementations** will return empty/default values
3. **Labels** will print with available data (case number, material from folder)
4. **Configuration flag** (`abs_integration.enabled`) controls whether ABS calls are attempted
5. **Application continues normally** without ABS data - no blocking or errors

```python
# Example placeholder interface (Python pseudocode)
class ABSService:
    def get_case_info(self, case_id: str) -> Optional[CaseInfo]:
        """
        PLACEHOLDER: Returns None until ABS API documentation is available.
        When implemented, will return case details from ABS.
        """
        if not config.abs_integration.enabled:
            return None
        # TODO: Implement when API docs provided
        return None
    
    def is_available(self) -> bool:
        """Check if ABS integration is configured and available."""
        return config.abs_integration.enabled and self._test_connection()
```

---

## Core Workflows

### Initial Database Scan (First-Run or Resume)

> [!IMPORTANT]
> When pointing to an existing STL directory for the first time, the system must process potentially thousands of files. This workflow provides visibility into progress and allows the user to start matching immediately.

**Detection Logic:**
1. On startup, check if `stl_root_path` exists in database
2. If NO → First-time scan required
3. If YES → Check for files with status ≠ COMPLETE (resume partial scan)
4. If all COMPLETE → Normal listening mode (incremental only)

```
┌──────────────────────────────────────────────────────────────────────────┐
│  INITIAL DATABASE SCAN WORKFLOW                                          │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  PHASE 1: FILE DISCOVERY (fast, ~seconds)                                 │
│  ─────────────────────────────────────────                                │
│  • Recursively scan all directories under stl_root_path                   │
│  • Count STL files + sum total bytes                                      │
│  • Exclude already-processed files (status = COMPLETE)                    │
│  • Sort by file creation date (NEWEST FIRST)                              │
│  • Display immediately: "Found 12,453 STL files (42 GB) to process"      │
│                                                                           │
│  PHASE 2: EXTRACTION WITH PROGRESS                                        │
│  ─────────────────────────────────────                                    │
│  For each file (newest first):                                            │
│    1. Mark status = IN_PROGRESS                                           │
│    2. Extract descriptor                                                  │
│    3. Store in database                                                   │
│    4. Mark status = COMPLETE (or FAILED)                                  │
│    5. Update rolling average (bytes/second)                               │
│    6. Calculate ETA: remaining_bytes / avg_bytes_per_second               │
│                                                                           │
│  CONCURRENT: FileSystemWatcher active during extraction                   │
│  • New files added to queue (processed after current file)                │
│  • Seamless transition to listening mode when queue empty                 │
│                                                                           │
│  MATCHING AVAILABLE IMMEDIATELY                                           │
│  • User can match against already-extracted fingerprints                  │
│  • UI shows: "Searching X of Y indexed units"                            │
│                                                                           │
└──────────────────────────────────────────────────────────────────────────┘
```

**Progress UI:**

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  INITIAL DATABASE SCAN                                              [PAUSE]  │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│  ████████████░░░░░░░░░░░░░░░░░░  27.4%                                        │
│                                                                                │
│  Files processed:  3,421 of 12,453                                            │
│  Data processed:   11.5 GB of 42 GB                                           │
│  Current file:     2024-0142/crown_14.stl                                     │
│                                                                                │
│  Processing speed: 5.2 MB/sec                                                 │
│  Time elapsed:     37 minutes                                                 │
│  Estimated remaining: 1 hour 38 minutes                                        │
│                                                                                │
│  ─────────────────────────────────────────────────────────────────────────    │
│  ℹ Matching is available now for 3,421 indexed units                         │
│  ─────────────────────────────────────────────────────────────────────────    │
│                                                                                │
└───────────────────────────────────────────────────────────────────────────────┘
```

**Pause/Resume Behavior:**

| Trigger | Behavior |
|---------|----------|
| **[PAUSE] button** | Stop after current file completes, persist state |
| **[RESUME] button** | Continue from next unprocessed file |
| **App shutdown** | Auto-pause, save state, show "Scan paused" on next launch |
| **App crash** | IN_PROGRESS file marked as PENDING on restart, resume from there |
| **App relaunch after pause** | Show notification: "Initial scan paused at 27.4%. [Resume] [Dismiss]" |

**Resume Notification:**

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  ℹ INITIAL SCAN INCOMPLETE                                                   │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│  The initial database scan was interrupted.                                   │
│                                                                                │
│  Progress: 3,421 of 12,453 files (27.4%)                                      │
│  Remaining: ~9,032 files (~30.5 GB)                                           │
│                                                                                │
│  [Resume Now]    [Resume Later]    [Start Fresh]                              │
│                                                                                │
└───────────────────────────────────────────────────────────────────────────────┘
```

**Database Schema for Tracking:**

```sql
-- Extraction status tracking
CREATE TABLE extraction_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stl_path TEXT NOT NULL UNIQUE,
    file_size_bytes INTEGER NOT NULL,
    file_created_at TIMESTAMP NOT NULL,     -- for priority sorting
    status TEXT NOT NULL DEFAULT 'PENDING', -- PENDING, IN_PROGRESS, COMPLETE, FAILED
    error_message TEXT,                      -- if FAILED
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    
    CHECK (status IN ('PENDING', 'IN_PROGRESS', 'COMPLETE', 'FAILED'))
);

CREATE INDEX idx_extraction_status ON extraction_queue(status);
CREATE INDEX idx_extraction_created ON extraction_queue(file_created_at DESC);
```

---

### 1. STL Database Registration (Background Process)

```
┌─────────────────────────────────────────────────────────────────────┐
│  STL Monitor Service - Runs Continuously                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. Watch [stl_root_path]/[Material]/[CaseName]/*.stl               │
│                    │                                                 │
│                    ▼                                                 │
│  2. New STL detected                                                 │
│                    │                                                 │
│                    ▼                                                 │
│  3. Parse STL → Extract point cloud                                  │
│                    │                                                 │
│                    ▼                                                 │
│  4. Compute 3D descriptors (AUM V2 GeoTransformer)                   │
│     • 10.3M Parameter Deep Neural Network                            │
│     • 5-Stage KPConv feature extraction                              │
│     • 6-Layer Flash Attention Transformer sequence                   │
│                    │                                                 │
│                    ▼                                                 │
│  5. Store in fingerprint database:                                   │
│     • Case ID (from folder name)                                     │
│     • Original STL path                                              │
│     • Descriptor vectors                                             │
│     • Timestamp                                                      │
│                    │                                                 │
│                    ▼                                                 │
│  6. Attempt ABS API call (background, non-blocking):                 │
│     • If ABS enabled and successful: cache case info locally         │
│     • If ABS disabled: skip (use folder-derived info only)           │
│     • If slow/failed: mark pending, retry later                      │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 2. Unit Matching (Operator-Initiated)

```
┌─────────────────────────────────────────────────────────────────────┐
│  Matching Workflow - Triggered by Physical Button                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. Operator places crown on platform (on its side, use putty)      │
│                    │                                                 │
│                    ▼                                                 │
│  2. Operator presses tactile button                                  │
│                    │                                                 │
│                    ▼                                                 │
│  3. Platform rotates ~20° while cameras/projectors capture          │
│     • Projectors alternate to avoid interference                     │
│     • Multiple frames captured during rotation                       │
│                    │                                                 │
│                    ▼                                                 │
│  4. Point cloud generated from structured light data                 │
│                    │                                                 │
│                    ▼                                                 │
│  5. Descriptors computed (AUM V2 GeoTransformer inference)           │
│                    │                                                 │
│                    ▼                                                 │
│  6. Query database for matches                                       │
│     • Scale-invariant comparison                                     │
│     • Return TOP 5 ranked results with confidence scores             │
│                    │                                                 │
│         ┌─────────┴─────────┐                                        │
│         ▼                   ▼                                        │
│  ┌─────────────┐     ┌─────────────┐                                │
│  │ MATCHES     │     │  NO MATCH   │                                │
│  │ FOUND       │     │             │                                │
│  └──────┬──────┘     └──────┬──────┘                                │
│         │                   │                                        │
│         ▼                   ▼                                        │
│  • Play success sound  • Play distinct alert sound                   │
│  • Display TOP 5       • Display "No Match" message                  │
│  • User must SELECT    • Suggest manual lookup                       │
│    correct match                                                     │
│         │                                                            │
│         ▼                                                            │
│  7. Operator confirms match                                          │
│         │                                                            │
│         ▼                                                            │
│  8. Print label with case information                                │
│     • Uses ABS cached data if available                              │
│     • Falls back to folder-derived data if ABS unavailable           │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 3. AI Model Training Pipeline (WSL2/Docker)

```
┌─────────────────────────────────────────────────────────────────────┐
│  V2 GeoTransformer Training Workflow                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. Dataset Transfer (Windows -> WSL)                                │
│     • Raw STLs are transferred over the network to the native        │
│       Linux disk at: \\wsl.localhost\Ubuntu\home\AUM_Dataset         │
│                    │                                                 │
│                    ▼                                                 │
│  2. Voxel Grid Preprocessing (preprocess.py)                         │
│     • Parses 20,000+ STLs into PyTorch tensors                       │
│     • Downsamples geometry to <8192 vertices                         │
│     • Saves fast-loading .pt binaries to /home/AUM_Dataset_PT        │
│                    │                                                 │
│                    ▼                                                 │
│  3. Heavyweight Training (train.py)                                  │
│     • Optimizes the 10.3M Parameter GeoTransformer                   │
│     • gradient_accumulation=16 prevents PCIe/VRAM swapping           │
│     • Generates State-of-the-Art network checkpoints                 │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## User Interface Requirements

### Main Application Window

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  Auto Unit Matcher                    Operator: John Smith (12345) [LOGOUT]   │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│  ┌──────────────────────────────────────┐  ┌────────────────────────────────┐ │
│  │                                      │  │  STATUS                        │ │
│  │                                      │  │  ──────────────────────────    │ │
│  │                                      │  │                                │ │
│  │           3D VIEWER                  │  │  ● Ready to scan               │ │
│  │                                      │  │                                │ │
│  │   (Rotatable view of scanned         │  │  Scanner 1: Connected          │ │
│  │    unit + matched STL overlay)       │  │  Scanner 2: Connected          │ │
│  │                                      │  │  Database: 12,453 units        │ │
│  │                                      │  │  ABS: Not configured           │ │
│  │                                      │  │  Last scan: 2 min ago          │ │
│  │                                      │  │                                │ │
│  │                                      │  ├────────────────────────────────┤ │
│  │                                      │  │  MATCH RESULT                  │ │
│  │                                      │  │  ──────────────────────────    │ │
│  └──────────────────────────────────────┘  │                                │ │
│                                            │  Case: 2024-0142               │ │
│  ┌──────────────────────────────────────┐  │  Patient: Smith, John          │ │
│  │                                      │  │  Material: Zirconia HT         │ │
│  │  [CONFIRM MATCH]        [REJECT]     │  │  Tooth: #14 Crown              │ │
│  │       (Enter)           (Escape)     │  │  Confidence: 98.7%             │ │
│  │                                      │  │                                │ │
│  │  [MANUAL LOOKUP]  [STATISTICS]       │  │  ⚠ Low confidence warning      │ │
│  │       (M)             (T)            │  │    (if applicable)             │ │
│  │                                      │  │                                │ │
│  │  [SETTINGS]  [PRINT QUEUE]  [?]      │  └────────────────────────────────┘ │
│  │     (S)                   (H)        │                                     │
│  └──────────────────────────────────────┘                                     │
│                                                                                │
│  ════════════════════════════════════════════════════════════════════════════ │
│  Station: Station-01 │ Disk: 156 GB free │ ⚠ 3 Extraction Failures │ [?]     │
└───────────────────────────────────────────────────────────────────────────────┘
```

### UI Requirements

| Element | Requirement |
|---------|-------------|
| **Header Bar** | Shows current operator name, employee number, logout button |
| **3D Viewer** | Interactive, mouse-drag rotation, zoom with scroll wheel |
| **Status Panel** | Scanner 1 & 2 status, database stats, ABS status, last scan time |
| **Match Result** | Case number, patient name, material, tooth info, confidence % |
| **Low Confidence Warning** | Yellow banner when confidence is below threshold |
| **Confirm Button** | Large, obvious; triggers label print. Shortcut: `Enter` |
| **Reject Button** | Marks as no-match, logs for review. Shortcut: `Escape` |
| **Manual Lookup** | Search database by case number. Shortcut: `M` |
| **Statistics** | Open statistics dashboard. Shortcut: `T` |
| **Settings** | Configuration (thresholds, backup, email alerts). Shortcut: `S` |
| **Print Queue** | View/print deferred labels when printer was offline |
| **Help [?]** | Show keyboard shortcuts. Shortcut: `H` or `?` |
| **Status Bar** | Station ID, disk space (color-coded), extraction failures indicator |
| **Extraction Failures** | Click to view/retry failed STL extractions |

### Audio Feedback

| Event | Sound | Purpose |
|-------|-------|---------|
| Match found | Pleasant chime/success tone | Immediate positive feedback |
| No match | Distinct alert/warning tone | Immediately alerts operator to problem |
| System error | Different alert tone | Hardware/software issues |

### Keyboard Shortcuts

| Action | Shortcut | Notes |
|--------|----------|-------|
| Initiate scan | `Spacebar` | Primary action |
| Confirm match | `Enter` | After scan completes |
| Reject match | `Escape` | Cancel current match |
| Manual lookup | `M` | Open search dialog |
| Settings | `S` | Open settings |
| Statistics | `T` | Open statistics dashboard |
| Help/Shortcuts | `H` or `?` | Show shortcut list |

> [!TIP]
> A "Show Shortcuts" button in the UI will display the complete list of available keyboard shortcuts.

---

## Security & Access Control

### HIPAA Compliance

> [!CAUTION]
> Patient data (names) is subject to HIPAA privacy regulations. The system must implement appropriate safeguards.

| Requirement | Implementation |
|-------------|----------------|
| **Access control** | Employee login required before any operation |
| **Audit trail** | All actions logged with operator, timestamp, station, scanner |
| **Automatic logout** | 10-minute inactivity timeout |
| **Data minimization** | Only necessary patient data displayed/printed |
| **Secure storage** | Database on secure server, not local workstations |

### User Authentication

```
┌─────────────────────────────────────────────────────────────────────┐
│  LOGIN SCREEN (shown when app starts or after timeout)              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│                    ┌─────────────────────────────┐                  │
│                    │      AUTO UNIT MATCHER      │                  │
│                    └─────────────────────────────┘                  │
│                                                                      │
│                    Employee Number: [__________]                     │
│                                                                      │
│                         [  LOG IN  ]                                │
│                                                                      │
│  ──────────────────────────────────────────────────────────────────  │
│  Status: Logged out - Please enter employee number                   │
└─────────────────────────────────────────────────────────────────────┘
```

**User Management:**
- Admin interface to add/edit/deactivate users
- Required fields: Employee Name, Employee Number
- Login via Employee Number only (no password - physical access control assumed)
- Users cannot delete themselves

**Session Management:**
- Auto-logout after 10 minutes of inactivity
- UI becomes inactive (grayed out) when logged out
- Current operator displayed in status bar when logged in

### Audit Trail

Every action is logged with:

| Field | Description |
|-------|-------------|
| `timestamp` | When the action occurred |
| `employee_number` | Who performed the action |
| `employee_name` | Name of operator (for reports) |
| `station_id` | Which station (for multi-station setup) |
| `scanner_id` | Which scanner (1 or 2) |
| `action_type` | SCAN, CONFIRM, REJECT, REPRINT, LOGIN, LOGOUT, etc. |
| `case_id` | Associated case (if applicable) |
| `details` | Additional context |

```sql
-- Audit log table
CREATE TABLE audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,
    scanner_id TEXT,                      -- NULL for non-scan actions
    action_type TEXT NOT NULL,
    case_id TEXT,                         -- NULL for non-case actions
    details TEXT,
    
    FOREIGN KEY (employee_number) REFERENCES users(employee_number)
);

CREATE INDEX idx_audit_timestamp ON audit_log(timestamp);
CREATE INDEX idx_audit_employee ON audit_log(employee_number);

-- Users table
CREATE TABLE users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_number TEXT NOT NULL UNIQUE,
    employee_name TEXT NOT NULL,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

---

## Database Schema

### Fingerprint Database (SQLite)

```sql
-- Units table: stores fingerprint data for each restoration (minimal, per user request)
CREATE TABLE units (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,              -- e.g., "2024-0142" (from folder name)
    stl_path TEXT NOT NULL,              -- full path to original STL
    descriptor_blob BLOB NOT NULL,       -- serialized 3D descriptors (~50-100KB per unit)
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(stl_path)                     -- prevent duplicate entries
);

CREATE INDEX idx_case_id ON units(case_id);

-- ABS cache table: stores data retrieved from ABS API (placeholder until API available)
CREATE TABLE abs_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL UNIQUE,
    due_date TEXT,
    product_name TEXT,
    patient_name TEXT,
    dr_name TEXT,
    material TEXT,
    tooth_info TEXT,
    rx_notes TEXT,
    shipping_method TEXT,
    cached_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    is_stale BOOLEAN DEFAULT FALSE       -- set TRUE if >24 hours old
);

-- Scan history table: audit log (retained 1.5 years)
CREATE TABLE scan_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    scanned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,        -- who performed the scan
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,             -- which station
    scanner_id TEXT NOT NULL,             -- which scanner (1 or 2)
    matched_unit_id INTEGER,              -- NULL if no match
    confidence REAL,
    confirmed BOOLEAN,                    -- operator confirmed?
    is_duplicate BOOLEAN DEFAULT FALSE,   -- detected as duplicate?
    is_reprint BOOLEAN DEFAULT FALSE,     -- was this just a reprint?
    operator_notes TEXT,
    
    FOREIGN KEY (matched_unit_id) REFERENCES units(id),
    FOREIGN KEY (employee_number) REFERENCES users(employee_number)
);

CREATE INDEX idx_scan_history_date ON scan_history(scanned_at);
CREATE INDEX idx_scan_history_unit ON scan_history(matched_unit_id);
```

### Storage Estimates

| Item | Size per unit | 1000 units/day | 1.5 years (~550 days) |
|------|---------------|----------------|----------------------|
| FPFH / AI descriptors | ~75 KB avg | ~75 MB/day | **~42 GB** |
| Metadata (SQLite) | ~1 KB | ~1 MB/day | ~550 MB |
| **Total** | | | **~43 GB** |

> [!NOTE]
> 1.5 years of data is easily stored on a standard 500GB+ drive.

---

## Label Format

Printed labels will include:

| Field | Source | Fallback (if ABS unavailable) |
|-------|--------|-------------------------------|
| Case number | Folder name | Folder name |
| Barcode | Generated from case number | Generated from case number |
| Due date | ABS API | "N/A" or blank |
| Product name | ABS API | "N/A" or blank |
| Patient name | ABS API | Extracted from folder name if possible |
| Dr Name | ABS API | "N/A" or blank |
| Material | Folder name | Folder name |
| Tooth number/description | ABS API | "N/A" or blank |
| RX notes | ABS API | "N/A" or blank |
| Shipping method | ABS API | "N/A" or blank |

---

## Error Handling & Recovery

### Scan Errors

| Error | Handling | User Message |
|-------|----------|--------------|
| **Camera failure** | Purge captured data, alert user | "Scan failed: Camera error. Please check camera connection and try again." |
| **Platform jam** | Stop rotation, alert user | "Scan failed: Platform rotation error. Please check for obstructions." |
| **Projector failure** | Abort scan, alert user | "Scan failed: Projector error. Please verify projector is working." |
| **Incomplete capture** | Retry scan, alert if persistent | "Scan incomplete. Retrying... [or] Please reposition unit and try again." |

> [!IMPORTANT]
> All error messages must be **meaningful and detailed** so operators can take corrective action.

### STL Extraction Failures

When descriptor extraction fails for STL files:

```
┌─────────────────────────────────────────────────────────────────────┐
│  EXTRACTION FAILURES INDICATOR (shown in status bar)                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Status Bar: [...other status...] │ ⚠ 3 Extraction Failures        │
│                                      └── Click to view details      │
│                                                                      │
│  CLICKING OPENS:                                                     │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  Extraction Failures                              [Retry All] │ │
│  ├────────────────────────────────────────────────────────────────┤ │
│  │  File                    │ Error                   │ Time     │ │
│  │  ─────────────────────── │ ─────────────────────── │ ──────── │ │
│  │  2024-0142/crown_14.stl  │ Invalid mesh geometry   │ 10:32 AM │ │
│  │  2024-0143/bridge.stl    │ File corrupted          │ 10:45 AM │ │
│  │  2024-0144/crown_3.stl   │ Zero vertices           │ 11:02 AM │ │
│  │                          │                         │          │ │
│  │  [Retry Selected]  [Clear Resolved]  [Export Log]              │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Printer Offline Handling

If label printer is offline when match is confirmed:

1. Alert user: "Printer offline. Please create a manual note with case number."
2. UI provides case number prominently for manual note creation
3. **Deferred Print Queue**: UI includes field to enter case number for later label printing
4. When printer comes online, user can print labels for queued cases

```
┌─────────────────────────────────────────────────────────────────────┐
│  DEFERRED PRINT QUEUE                                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Enter case number for deferred printing: [____________] [Add]      │
│                                                                      │
│  Queued for printing:                                                │
│  • 2024-0142 (added 10:32 AM)                                       │
│  • 2024-0143 (added 10:45 AM)                                       │
│                                                                      │
│  [Print All]  [Clear Queue]                                         │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Database Corruption

**Detection:** Automatic integrity check on application startup and hourly during operation.

**Recovery procedure:**
1. Detect corruption via `PRAGMA integrity_check` (SQLite) or PostgreSQL health checks
2. Alert administrator immediately
3. **Refuse to operate** until backup is restored
4. Provide instructions for backup restoration

---

## Duplicate & Remilling Detection

### Duplicate Scan Detection

If a scanned unit matches a fingerprint that was already confirmed by any user from any session within the last **5 hours**:

```
┌─────────────────────────────────────────────────────────────────────┐
│  ⚠ DUPLICATE DETECTION                                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  This unit was already scanned at 2:34 PM by John Smith.            │
│                                                                      │
│  This may indicate:                                                  │
│  • Accidental double-scan                                            │
│  • Unit fell back into pile                                          │
│  • Double-milled unit (manufacturing error)                          │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │  ☐ Remilling in progress (disable duplicate alerts for this    │ │
│  │    case until manually cleared)                                 │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│  [Reprint Label]    [Confirm as Duplicate]    [Cancel]              │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Options:**
- **Reprint Label**: Just reprint, no new scan history record
- **Confirm as Duplicate**: Log as intentional re-scan (with reason)
- **Cancel**: Abort operation

### Remilling Flag

When "Remilling in progress" is checked:
- A flag is set on the case ID in the database
- Duplicate alerts are **suppressed** for that case
- Flag remains active until manually cleared in Settings → Remilling Cases
- UI shows list of cases with active remilling flags

```sql
-- Add to units or create separate table
CREATE TABLE remilling_flags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL UNIQUE,
    flagged_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    flagged_by TEXT NOT NULL,             -- employee_number
    notes TEXT
);
```

---

## Concurrency Handling

### Multi-Scanner Operation

**Strategy:** Optimistic locking with time-window duplicate detection.

| Scenario | Handling |
|----------|----------|
| Two operators scan same unit simultaneously | Second operator sees duplicate alert after first confirms |
| Same scanner scanned twice in <5 hours | Duplicate detection alert triggered |
| Two different units match same fingerprint | Should never happen (indicates algorithm issue - log for review) |

### Database Locking (PostgreSQL)

| Operation | Locking Strategy |
|-----------|------------------|
| **Fingerprint insertion** (new STL) | Row-level, serializable isolation |
| **Scan history append** | No locking (append-only) |
| **Match queries** | Read-committed (no locks) |
| **ABS cache updates** | Row-level upsert |

```sql
-- Fingerprint insertion (rare contention)
INSERT INTO units (...) 
ON CONFLICT (stl_path) DO NOTHING;

-- Scan history (high volume, append-only)
INSERT INTO scan_history (...);  -- No locking needed
```

---

## Matching Algorithm Configuration

### Confidence Thresholds

| Threshold | Default | Configurable | Behavior |
|-----------|---------|--------------|----------|
| **No match** | <80% | Yes | "No match found" message |
| **Low confidence warning** | 80-90% | Yes | Show results with warning banner |
| **High confidence** | ≥90% | Yes | Normal display (green indicator) |

**When all top 5 have low confidence:**
- Show all 5 results
- Display warning: "All matches have low confidence. Please verify carefully."
- Require explicit confirmation before printing

### User-Configurable Settings

```yaml
matching:
  no_match_threshold: 80          # Below this = "no match"
  low_confidence_threshold: 90    # Below this = show warning
  high_confidence_threshold: 90   # At or above this = high confidence
  duplicate_detection_hours: 5    # Time window for duplicate alerts
```

---

## Performance Requirements

| Metric | Target | Rationale |
|--------|--------|-----------|
| **Daily throughput** | 1,000+ units | Current lab volume |
| **Time per unit** | ≤15 seconds | Includes scan + match + confirm |
| **Scan time** | ≤3 seconds | Platform rotation + capture |
| **Match time** | ≤1 second | Database query + ranking |
| **Error rate** | <1% | False matches or missed matches |
| **Database capacity** | 550,000+ units | 1.5 years of history |
| **Data retention** | 1.5 years | Per user requirement |

---

## Integration Points

### STL Database Structure (3Shape Output)

```
[stl_root_path]/                    # Configurable root path
└── [Material]/                     # e.g., "Zirconia HT", "PMMA"
    └── [CaseName]/                 # e.g., "2024-0142 Smith John"
        ├── restoration_1.stl
        ├── restoration_2.stl
        └── *.pts                   # (ignored)
```

> [!NOTE]
> The `stl_root_path` is configurable in application settings to allow changing the database location without code modification.

### Future Integration Opportunities

| Integration | Purpose | Priority | Status |
|-------------|---------|----------|--------|
| ABS Lab Management | Case info for labels | High | **Placeholder** - awaiting API docs |
| Client Portal | Status notifications to dentists | Low | Future |
| 3Shape Dental Manager | Direct case info sync | Medium | Future |
| Barcode/QR on labels | Downstream tracking | Low | Included |

---

## Modular Architecture for Multi-Station Expansion

```
                    ┌─────────────────────────┐
                    │   Shared Database       │
                    │   (Network share or     │
                    │    local server)        │
                    └───────────┬─────────────┘
                                │
            ┌───────────────────┼───────────────────┐
            │                   │                   │
            ▼                   ▼                   ▼
    ┌───────────────┐   ┌───────────────┐   ┌───────────────┐
    │  Station 1    │   │  Station 2    │   │  Station N    │
    │  ──────────── │   │  ──────────── │   │  ──────────── │
    │  • 1 PC       │   │  • 1 PC       │   │  • 1 PC       │
    │  • 2 Scanners │   │  • 2 Scanners │   │  • 2 Scanners │
    │  • 1 Printer  │   │  • 1 Printer  │   │  • 1 Printer  │
    └───────────────┘   └───────────────┘   └───────────────┘
```

**Architecture notes:**
- **1 Station = 1 PC + 2 Scanners + 1 Printer** (per user definition)
- Each PC can operate both scanners in parallel for higher throughput
- Fingerprint database is **shared across all stations**
- STL monitor runs on a dedicated server or one designated PC
- No inter-station dependencies for basic operation

---

## Development Phases

### Parallel Development Strategy

> [!IMPORTANT]
> **Algorithm development can proceed in parallel with hardware development** by using pre-captured test scans. This eliminates blocking dependencies and accelerates the project timeline.

```
┌─────────────────────────────────────────────────────────────────────────┐
│  PARALLEL DEVELOPMENT TRACKS                                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  TRACK A: ALGORITHM DEVELOPMENT              TRACK B: HARDWARE           │
│  (Can start immediately)                     (In parallel)               │
│                                                                          │
│  ┌──────────────────────────────┐           ┌─────────────────────────┐ │
│  │ 1. Collect test scans        │           │ 1. Research scanners    │ │
│  │    (existing lab scanner)    │           │ 2. Evaluate vendors     │ │
│  │ 2. Build fingerprint DB      │           │ 3. Acquire hardware     │ │
│  │ 3. Implement FPFH extraction │           │ 4. Develop drivers      │ │
│  │ 4. Build matching engine     │           │ 5. Integration testing  │ │
│  │ 5. Tune & validate accuracy  │           │                         │ │
│  └──────────────┬───────────────┘           └───────────┬─────────────┘ │
│                 │                                       │               │
│                 └───────────────────┬───────────────────┘               │
│                                     ▼                                    │
│                         ┌─────────────────────┐                         │
│                         │ INTEGRATION         │                         │
│                         │ Connect real scanner│                         │
│                         │ to proven algorithm │                         │
│                         └─────────────────────┘                         │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Test Data Collection

Pre-capture test scans using existing lab equipment (desktop scanner, intraoral scanner, or photogrammetry):

| Condition | Purpose | Quantity |
|-----------|---------|----------|
| **Clean, ideal scans** | Baseline accuracy testing | 50+ units |
| **Varied orientations** | Test rotation invariance | 20+ units × 3 orientations |
| **Partial/incomplete** | Test robustness | 20+ units |
| **Different materials** | Test cross-material matching | Zirconia, PMMA, metal |
| **Similar anatomies** | Test discrimination (hardest case) | 10+ pairs of "look-alike" units |

**Test data structure:**
```
test_data/
├── ground_truth.csv           # Maps scan files to original STL case IDs
├── scans/
│   ├── ideal/                 # Clean baseline scans
│   ├── rotated/               # Same units at different angles
│   ├── partial/               # Incomplete captures
│   └── challenging/           # Similar units, edge cases
└── stl_database/              # Corresponding original STL files
```

### Phase 1: Proof of Concept (Parallel Tracks)

**Track A - Algorithm (Start Immediately):**
- [ ] Collect test scans from existing scanner (50+ units)
- [ ] Implement STL → FPFH descriptor extraction
- [ ] Build matching engine with test scans
- [ ] Validate accuracy on known ground truth
- [ ] Tune parameters for >99% accuracy

**Track B - Hardware (In Parallel):**
- [ ] Research and evaluate scanner hardware
- [ ] Contact vendors, get quotes
- [ ] Acquire/build scanning station

### Phase 2: Core Application
- [ ] STL monitor service
- [ ] Scanner driver integration (when hardware ready)
- [ ] Matching engine (from Phase 1 algorithm)
- [ ] Basic UI (no 3D viewer)
- [ ] Label printing (without ABS data)
- [ ] **ABS placeholder interface** (stubs ready for implementation)

### Phase 3: Full Application
- [ ] 3D viewer integration
- [ ] Audio feedback
- [ ] Configuration UI
- [ ] Error handling and logging
- [ ] Performance optimization
- [ ] **ABS integration** (when API docs provided)

### Phase 4: Production Deployment
- [ ] Multi-station support
- [ ] Backup/recovery procedures
- [ ] Operator training materials
- [ ] Monitoring and alerting

---

## Deployment & Installation

### Installer

| Item | Specification |
|------|---------------|
| **Format** | MSI installer package |
| **Prerequisites** | .NET 8.0 Runtime, Visual C++ Redistributable |
| **Install location** | `C:\Program Files\Auto Unit Matcher\` |
| **Data location** | `C:\ProgramData\AUM\` (logs, local cache) |

### Auto-Update

- Application checks for updates on startup (configurable)
- Downloads updates in background
- Prompts user to restart when update is ready
- Update server URL configurable in settings

### Station Configuration

On first run, administrator configures:
- Station ID (e.g., "Station-01")
- Database server connection string
- Backup server location
- Email for alerts

---

## Logging & Monitoring

### Log Storage

| Log Type | Location | Retention |
|----------|----------|-----------|
| **Application logs** | `[app_root]\logs\` | 30 days |
| **Scan history** | Database | 1.5 years |
| **Audit trail** | Database | 1.5 years |
| **Error logs** | `[app_root]\logs\errors\` | 30 days |

### Log Rotation Policy

- Max log file size: **50 MB**
- Rotate when size exceeded
- Keep last **30 days** of logs
- Older logs automatically deleted (or compressed if configured)

### Alert System

**Email alerts** (configurable recipient):

| Trigger | Threshold | Notes |
|---------|-----------|-------|
| High error rate | >10 errors in 1 hour | Indicates hardware/software issue |
| Low scan rate | <5 scans/hour when previously active | May indicate equipment problem |
| Disk space low | <10 GB free | Database may fill up |
| Database health check failed | Any failure | Immediate action required |

**Email policy:**
- Maximum **1 email per session** (sent at session end if alerts occurred)
- No email if session had no alerts
- Summary includes all alert types that occurred

### Disk Space Monitoring

- Warning at **20 GB** free: Yellow indicator in status bar
- Critical at **10 GB** free: Red indicator, email alert sent
- Calculate projected days until full based on current growth rate

---

## Backup & Disaster Recovery

### Backup Schedule

| Item | Frequency | Retention |
|------|-----------|-----------|
| **Fingerprint database** | Daily | 30 days |
| **Configuration** | Daily | 30 days |
| **Audit logs** | Daily | 1.5 years |

**Configurable settings:**
- Backup time of day (default: 2:00 AM)
- Backup server location (network path)

### Backup Location

```yaml
backup:
  server_path: "\\\\server\\backups\\aum\\"  # CONFIGURABLE
  time_of_day: "02:00"                        # CONFIGURABLE
  retention_days: 30
```

### Restore Procedure

1. Stop AUM application on all stations
2. Navigate to Settings → Backup & Recovery
3. Select backup date to restore
4. Confirm restore (warns about data loss since backup)
5. Application restarts with restored data

> [!WARNING]
> If STL source files are deleted but fingerprints exist, matching still works. The fingerprint is self-contained.

---

## Statistics Dashboard

Access via **Statistics** button (or `T` shortcut):

```
┌─────────────────────────────────────────────────────────────────────┐
│  STATISTICS DASHBOARD                                    [X Close]  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  TODAY'S SUMMARY                                                     │
│  ─────────────────────────────────────────────────────────────────── │
│  Total scans: 342          Successful matches: 338 (98.8%)          │
│  No matches: 4             Average confidence: 94.2%                │
│  Duplicates detected: 7    Reprints: 12                             │
│                                                                      │
│  THROUGHPUT (units per hour)                                         │
│  ─────────────────────────────────────────────────────────────────── │
│                                                                      │
│  60 ┤                    ████                                        │
│  50 ┤                ████████████                                    │
│  40 ┤        ████████████████████████████                           │
│  30 ┤    ████████████████████████████████████                       │
│  20 ┤████████████████████████████████████████                       │
│     └────┬────┬────┬────┬────┬────┬────┬────┬─────                  │
│         8AM  9AM 10AM 11AM 12PM  1PM  2PM  3PM                       │
│                                                                      │
│  OPERATOR PERFORMANCE (this session)                                 │
│  ─────────────────────────────────────────────────────────────────── │
│  John Smith (12345): 28 units/hr avg, 156 total                     │
│  Jane Doe (12346): 32 units/hr avg, 186 total                       │
│                                                                      │
│  [Export Report]  [View History]  [Operator Details]                │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Metrics tracked:**
- Daily/weekly/monthly scan counts
- Success/failure rates
- Average confidence scores
- Throughput per hour (graphed over time)
- Per-operator statistics (units scanned, rate over time)
- Duplicate detection counts

---

## Scanner Calibration

### Calibration Procedure

Required equipment: **Calibration target** (known geometry artifact kept at each station)

**Calibration steps:**
1. Place calibration target on platform
2. Navigate to Settings → Scanner Calibration
3. Click "Run Calibration"
4. System scans target, compares to known geometry
5. Computes correction factors
6. Reports PASS / WARN / FAIL

### Calibration Schedule

| Check | Frequency | Action |
|-------|-----------|--------|
| **Daily auto-check** | On first scan of day | Quick verification, alert if drift detected |
| **Full calibration** | Weekly (or on-demand) | Complete recalibration with correction factors |
| **Post-maintenance** | After any hardware work | Required before resuming operation |

### Calibration Results

| Result | Meaning | Action |
|--------|---------|--------|
| **PASS** | Within tolerance | Normal operation |
| **WARN** | Slight drift | Monitor closely, schedule maintenance |
| **FAIL** | Exceeds tolerance | Hardware re-alignment required, refuse to scan |

> [!NOTE]
> If cameras physically drift out of alignment, calibration will detect this and mark as FAIL. Hardware adjustment is required.

---

## Open Questions

> [!IMPORTANT]
> The following items require input before development begins:

1. ~~**Hardware acquisition:**~~ ✅ User confirmed: Research specific scanner models with vendor contacts/pricing.

2. ~~**Label format:**~~ ✅ Resolved: Case number, barcode, due date, product name, patient name, Dr name, material, tooth info, RX notes, shipping method.

3. ~~**Multiple candidates:**~~ ✅ Resolved: Show top 5 matches, require user to choose.

4. ~~**History retention:**~~ ✅ Resolved: 1.5 years.

5. ~~**Offline operation:**~~ ✅ Resolved: System will **refuse to operate** if shared database is unavailable. No offline queueing.

6. ~~**ABS integration:**~~ ✅ Resolved: Implement as placeholders until API documentation is provided. Application continues without ABS data.

---

## Verification Plan

### Accuracy Testing
- Test matching accuracy on 500+ known units
- Measure false positive and false negative rates
- Test with "similar" units (same material, similar anatomy)

### Performance Testing
- Time full workflow from button press to label print
- Test with database of 100,000+ units
- Measure under continuous operation (100 units/hour)

### Usability Testing
- Operator trials with actual lab staff
- Measure time-to-proficiency
- Collect feedback on UI and workflow

---

*Document Version: 2.1*
*Created: February 7, 2026*
*Last Updated: February 8, 2026*

## Revision History

| Version | Date | Changes |
|---------|------|--------|
| 1.0 | Feb 7, 2026 | Initial draft |
| 1.1 | Feb 7, 2026 | Added ABS integration, tech stack, corrected station architecture, updated label format, simplified database schema |
| 1.2 | Feb 7, 2026 | Added ABS placeholder strategy, label fallback values, clarified development phases |
| 1.3 | Feb 7, 2026 | Added Development Environment Setup section with vcpkg, CMake, project structure |
| 2.0 | Feb 7, 2026 | Major update from gap analysis: Added Security & Access Control (HIPAA, user auth, audit trail), Error Handling & Recovery, Duplicate & Remilling Detection, Concurrency Handling, Matching Algorithm Configuration, Deployment & Installation (MSI, auto-update), Logging & Monitoring (log rotation, email alerts), Backup & Disaster Recovery, Statistics Dashboard, Scanner Calibration |
| 2.1 | Feb 8, 2026 | Added Initial Database Scan workflow with progress tracking, pause/resume, crash recovery, ETA calculation (bytes/second), newest-first priority, matching during extraction |


