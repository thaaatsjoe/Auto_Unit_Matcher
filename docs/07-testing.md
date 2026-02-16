# Brain 7: Testing & Validation

## Current Test Coverage

### C# Tests (`tests/AUM.Tests/`)

**Framework**: xUnit with `IAsyncLifetime` for async setup/teardown.
**Database**: In-memory SQLite (`DatabaseContext.CreateInMemory()`).

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `DatabaseContextTests.cs` | ~5 | Schema init, integrity check, WAL mode, version |
| `UnitRepositoryTests.cs` | 7 | Add, GetById, GetByStlPath, GetByCaseId, Exists, GetAllDescriptors, Delete, Count |
| `UserRepositoryTests.cs` | ~5 | Add, GetByEmployeeNumber, GetAllActive, Deactivate |
| `ScanHistoryRepositoryTests.cs` | ~6 | Add, GetRecent, GetByUnitId, duplicate detection within 5-hour window |
| `ExtractionQueueRepositoryTests.cs` | ~6 | Enqueue, DequeueNext, MarkCompleted, MarkFailed, GetPendingCount, GetFailed |
| `SanityTests.cs` | ~2 | Basic application sanity checks |

**Total**: ~31 test cases

### Test Pattern

```csharp
[Trait("Category", "Database")]
public class XRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseContext _context;     // In-memory
    private readonly XRepository _repository;

    public XRepositoryTests() {
        _context = DatabaseContext.CreateInMemory();
        _repository = new XRepository(_context);
    }

    public async Task InitializeAsync() {
        await _context.InitializeAsync();          // Create schema
    }

    public Task DisposeAsync() {
        _context.Dispose();                        // Cleanup
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MethodName_ExpectedBehavior() {
        // Arrange → Act → Assert
    }
}
```

### Running C# Tests

```powershell
cd "c:\...\Auto_Unit_Matcher"
dotnet test
```

### C++ Tests (`src/AUM.Engine/tests/`)

**Framework**: Google Test (GTest) via vcpkg.
**Status**: Directory structure exists but test content needs verification.

```powershell
cd src/AUM.Engine/build
ctest --output-on-failure
```

---

## What's NOT Tested

### Missing Test Categories

| Category | Gap | Risk |
|----------|-----|------|
| **Matching pipeline integration** | No tests for MatchingService end-to-end (FAISS voting → RANSAC+ICP → ranking) | Core business logic is unvalidated |
| **C++ descriptor extraction** | No verified C++ unit tests for ISS+SHOT352 extraction | Silent algorithm bugs possible |
| **Interop boundary** | No tests verifying P/Invoke marshalling correctness | Handle leaks, data corruption possible |
| **StlMonitorService** | No tests for FileSystemWatcher behavior | Race conditions, missed files |
| **UI ViewModels** | No ViewModel unit tests | UI state management bugs |
| **AuditService** | No tests for audit context propagation | HIPAA compliance gap |
| **UnitService** | No integration tests for register → index → match flow | End-to-end workflow unvalidated |
| **Performance** | No benchmarks for extraction, matching, or DB queries at scale | Unknown behavior at 550K units |
| **Error paths** | Limited testing of failure scenarios (corrupt STL, disk full, etc.) | Crash risk in production |

### What Would Be Needed for Production Confidence

1. **Accuracy benchmark**: Run matching against a labeled test set of dental STLs with known correct matches. Measure precision@1, precision@5, and recall.
2. **Partial scan test**: Verify that a partial scan of a known crown produces a correct match with acceptable confidence.
3. **Scale test**: Load 100K+ synthetic descriptors, measure query latency and memory usage.
4. **Stress test**: Rapid sequential scans to verify concurrency handling and duplicate detection.
5. **Interop leak test**: Long-running test to verify no handle/memory leaks across the P/Invoke boundary.

---

## Testing Infrastructure Strengths

- **In-memory SQLite**: Tests are fast, isolated, and don't require file cleanup
- **IAsyncLifetime**: Proper async setup/teardown prevents test pollution
- **Trait categories**: Tests are categorized ("Database") for selective execution
- **Repository coverage**: All 5 repository test files follow the same correct pattern
- **Arrange-Act-Assert**: Consistent test structure across all files

## Testing Infrastructure Gaps

- **No mocking framework**: Services aren't tested with mocked dependencies
- **No test fixtures**: No shared test data or factory methods for creating test objects
- **No CI/CD configuration**: No `.github/workflows` or `azure-pipelines.yml`
- **No code coverage reporting**: No coverage tool configured
- **No integration test project**: All tests are in one project, no separation between unit and integration tests
