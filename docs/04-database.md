# Brain 4: Database

## Overview

AUM uses a single **SQLite** database file with **WAL mode** enabled for concurrent read/write access. Managed by `DatabaseContext` (singleton).

**Connection pattern**: Single persistent connection per application instance, configured with WAL journal mode for readers not blocking writers.

## Schema

### `units` Table

The core table. Stores every registered dental unit with its 3D fingerprint.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | Database ID (used as FAISS vector ID) |
| `case_id` | TEXT | NOT NULL, INDEXED | Case identifier from folder name (e.g., "2024-0142") |
| `stl_path` | TEXT | NOT NULL, UNIQUE | Full path to original STL file |
| `descriptor_blob` | BLOB | NOT NULL | Serialized ISS+SHOT352 descriptors in SH01 format (~900 KB–1.1 MB typical) |
| `created_at` | TEXT | NOT NULL | ISO 8601 UTC timestamp |

**Index**: `idx_units_case_id` on `case_id` for case-based lookups.
**Index**: `idx_units_stl_path` on `stl_path` for duplicate detection.

### `users` Table

Employee authentication. No passwords — uses employee number only.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `employee_number` | TEXT | NOT NULL, UNIQUE | Login identifier |
| `employee_name` | TEXT | NOT NULL | Display name |
| `is_active` | INTEGER | NOT NULL, DEFAULT 1 | Soft delete (0 = deactivated) |
| `created_at` | TEXT | NOT NULL | |

### `audit_log` Table

HIPAA-compliant action log. Append-only.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `timestamp` | TEXT | NOT NULL, INDEXED | ISO 8601 UTC |
| `employee_number` | TEXT | NOT NULL, INDEXED | Who performed the action |
| `employee_name` | TEXT | NOT NULL | Denormalized for report readability |
| `station_id` | TEXT | NOT NULL | Workstation identifier |
| `action_type` | TEXT | NOT NULL | Action code (e.g., LOGIN, MATCH_CONFIRMED) |
| `case_id` | TEXT | | Related case, if applicable |
| `details` | TEXT | | Additional context |

**Index**: `idx_audit_timestamp` for date-range queries.
**Index**: `idx_audit_employee` for per-employee reports.

### `scan_history` Table

Record of every scan operation. Retained for 1.5 years per PRD.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `scanned_at` | TEXT | NOT NULL, INDEXED | ISO 8601 UTC |
| `employee_number` | TEXT | NOT NULL | |
| `employee_name` | TEXT | NOT NULL | |
| `station_id` | TEXT | NOT NULL | |
| `scanner_id` | TEXT | NOT NULL | Which scanner (1 or 2) |
| `matched_unit_id` | INTEGER | NULLABLE | FK to units.id, null if no match |
| `confidence` | REAL | NULLABLE | Match confidence 0-100 |
| `confirmed` | INTEGER | NULLABLE | Operator confirmation (0/1/null) |
| `is_duplicate` | INTEGER | NOT NULL, DEFAULT 0 | Duplicate detection flag |
| `is_reprint` | INTEGER | NOT NULL, DEFAULT 0 | Reprint vs. new scan |
| `operator_notes` | TEXT | NULLABLE | |

### `extraction_queue` Table

Tracks pending and completed STL extraction jobs.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `stl_path` | TEXT | NOT NULL, UNIQUE | File being processed |
| `case_id` | TEXT | NOT NULL | |
| `status` | TEXT | NOT NULL, DEFAULT 'pending' | pending, processing, completed, failed |
| `error_message` | TEXT | NULLABLE | Failure details |
| `queued_at` | TEXT | NOT NULL | |
| `completed_at` | TEXT | NULLABLE | |
| `retry_count` | INTEGER | NOT NULL, DEFAULT 0 | |

### `abs_cache` Table

Cache for ABS Lab Management API responses (stubbed — not yet integrated).

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `case_id` | TEXT | NOT NULL, INDEXED | |
| `response_json` | TEXT | NOT NULL | Cached JSON payload |
| `cached_at` | TEXT | NOT NULL | |
| `expires_at` | TEXT | NOT NULL | Cache TTL |

### `remilling_flags` Table

Tracks cases flagged for remilling (replacement unit needed).

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | |
| `unit_id` | INTEGER | NOT NULL | FK to units.id |
| `reason` | TEXT | NOT NULL | Why remilling is needed |
| `flagged_by` | TEXT | NOT NULL | Employee number |
| `flagged_at` | TEXT | NOT NULL | |
| `resolved` | INTEGER | NOT NULL, DEFAULT 0 | |
| `resolved_at` | TEXT | NULLABLE | |

## Repository Pattern

Each table has a matching interface + implementation pair:

| Repository | Key Operations |
|-----------|----------------|
| `UnitRepository` | `AddAsync`, `GetByIdAsync`, `GetByStlPathAsync`, `GetByCaseIdAsync`, `UpsertByStlPathAsync`, `GetAllDescriptorsAsync`, `ExistsAsync`, `DeleteAsync`, `CountAsync` |
| `UserRepository` | `AddAsync`, `GetByEmployeeNumberAsync`, `GetAllActiveAsync`, `DeactivateAsync` |
| `AuditRepository` | `LogAsync`, `GetByDateRangeAsync`, `GetByEmployeeAsync` |
| `ScanHistoryRepository` | `AddAsync`, `GetRecentAsync`, `GetByUnitIdAsync`, `CheckDuplicateAsync` (5-hour window) |
| `ExtractionQueueRepository` | `EnqueueAsync`, `DequeueNextAsync`, `MarkCompletedAsync`, `MarkFailedAsync`, `GetPendingCountAsync`, `GetFailedAsync` |
| `AbsCacheRepository` | `GetAsync`, `SetAsync`, `IsExpiredAsync` |
| `RemillingFlagRepository` | `FlagAsync`, `GetByUnitIdAsync`, `ResolveAsync` |

All repositories use parameterized queries (SQL injection safe) and `IAsyncLifetime`-compatible patterns.

## Database Initialization

`DatabaseContext.InitializeAsync()`:
1. Open connection (or create file if first run)
2. Enable WAL mode: `PRAGMA journal_mode = WAL`
3. Check schema version: `PRAGMA user_version`
4. If outdated: execute migration SQL to create/alter tables
5. Run integrity check: `PRAGMA integrity_check`

## Performance Considerations

- **Descriptor blob size**: ~1 MB per unit (SH01 format: keypoint XYZ + SHOT352 features). At 20K units = ~20 GB. This is the dominant storage cost.
- **WAL mode**: Readers don't block writers, but only one writer at a time. Sufficient for single-station deployment.
- **No foreign keys enforced**: SQLite FK enforcement is off by default and not explicitly enabled. Referential integrity is managed by application logic.
- **Indexes**: Case ID and STL path are indexed for fast lookups. Audit and scan history have timestamp indexes for range queries.
