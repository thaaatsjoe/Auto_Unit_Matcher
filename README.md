# Auto Unit Matcher

Dental restoration identification system using 3D scanning and shape-matching technology.

## Project Structure

```
Auto_Unit_Matcher/
├── AUM.sln                 # Visual Studio solution
├── PRD.md                  # Product Requirements Document
├── src/
│   ├── AUM.Engine/         # C++ engine (PCL, FAISS)
│   │   ├── CMakeLists.txt
│   │   ├── vcpkg.json
│   │   ├── include/        # Headers
│   │   ├── src/            # Implementation
│   │   └── tests/          # GTest unit tests
│   ├── AUM.Core/           # C# core library
│   │   ├── Data/           # Database layer
│   │   ├── Services/       # Business services
│   │   ├── Native/         # P/Invoke wrappers
│   │   └── Models/         # Domain models
│   └── AUM.UI/             # WPF application
│       ├── Views/          # XAML views
│       ├── ViewModels/     # MVVM view models
│       └── Styles/         # Themes and styles
├── tests/
│   └── AUM.Tests/          # xUnit tests
└── docs/                   # Documentation
```

## Prerequisites

- Visual Studio 2022 (17.x+) with C++ and .NET workloads
- .NET SDK 8.0+
- vcpkg with packages: `pcl`, `faiss`, `sqlite3`, `gtest`
- CMake 3.20+

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

## Development Phases

See `implementation_plan.md` in the brain artifacts directory for the phased development plan.
