# Auto Unit Matcher (AUM)

Dental restoration identification system using 3D scanning and state-of-the-art shape-matching technology.

## Project Structure

```
Auto_Unit_Matcher/
├── AUM.sln                 # Visual Studio solution
├── PRD.md                  # Product Requirements Document
├── tools/
│   └── v2_training/        # V2 Deep Learning Pipeline (PyTorch)
│       ├── model.py        # 10.3M Parameter GeoTransformer + Flash Attention
│       ├── train.py        # WSL2 Accelerated Training Loop
│       ├── preprocess.py   # Voxel Grid Downsampling & Tensor Serialization
│       └── loss.py         # Point Matching & Circle Loss
├── src/
│   ├── AUM.Engine/         # Legacy V1 C++ engine (PCL, FAISS)
│   ├── AUM.Core/           # C# core library
│   └── AUM.UI/             # WPF application
├── tests/
└── docs/                   # Documentation
```

## Prerequisites

### Local C#/C++ App
- Visual Studio 2022 (17.x+) with C++ and .NET workloads
- .NET SDK 8.0+
- vcpkg with packages: `pcl`, `faiss`, `sqlite3`, `gtest`
- CMake 3.20+

### V2 Deep Learning Training (WSL2)
- Windows Subsystem for Linux (Ubuntu)
- Docker Engine (Native WSL via `apt-get`, bypassing Docker Desktop)
- Native Linux SSD directory for dataset (e.g. `/home/AUM_Dataset_PT`) to prevent $I/O$ bottlenecks.
- NVIDIA GPU (RTX 3080 Ti / 4090) with 12GB+ VRAM & Linux CUDA drivers.

## Building

### C++ Engine

```powershell
cd src/AUM.Engine
cmake -B build -S . -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake
cmake --build build --config Release
```

### C# Solution

```powershell
dotnet restore
dotnet build --configuration Release
```

### Run Tests

```powershell
# C# tests
dotnet test

# C++ tests (after building engine)
cd src/AUM.Engine/build
ctest --output-on-failure
```

## Training the AUM V2 Deep Learning Engine

The AUM V2 pipeline utilizes a native Linux filesystem within WSL2 to process terabytes of 3D data without Windows I/O bottlenecks.

1. **Transfer the Dataset:** Place your raw `.stl` training files into the WSL Ubuntu distribution at:
   `\\wsl.localhost\Ubuntu\home\AUM_Dataset`

2. **Preprocess (Voxel Downsampling):**
   ```bash
   wsl -d Ubuntu docker exec -it aum-training-v4 /bin/bash
   python preprocess.py
   ```
   *This serializes the STLs into optimized `.pt` tensors at `/home/AUM_Dataset_PT`.*

3. **Start the Heavyweight Training Loop:**
   ```bash
   python train.py --data_dir /home/AUM_Dataset_PT --batch_size 1 --grad_accum 16
   ```

## Development Phases

See `implementation_plan.md` in the brain artifacts directory for the phased development plan.
