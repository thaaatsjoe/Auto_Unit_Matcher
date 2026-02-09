-- AUM Database Schema v1.0
-- SQLite compatible

-- ============================================================================
-- Units table: stores fingerprint data for each restoration
-- ============================================================================
CREATE TABLE IF NOT EXISTS units (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,
    stl_path TEXT NOT NULL,
    descriptor_blob BLOB NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(stl_path)
);

CREATE INDEX IF NOT EXISTS idx_units_case_id ON units(case_id);

-- ============================================================================
-- Users table: operator accounts
-- ============================================================================
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_number TEXT NOT NULL UNIQUE,
    employee_name TEXT NOT NULL,
    is_active INTEGER DEFAULT 1,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- Audit log table: HIPAA-compliant action tracking
-- ============================================================================
CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,
    scanner_id TEXT,
    action_type TEXT NOT NULL,
    case_id TEXT,
    details TEXT,
    
    FOREIGN KEY (employee_number) REFERENCES users(employee_number)
);

CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_employee ON audit_log(employee_number);

-- ============================================================================
-- Scan history table: retained 1.5 years
-- ============================================================================
CREATE TABLE IF NOT EXISTS scan_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    scanned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,
    scanner_id TEXT NOT NULL,
    matched_unit_id INTEGER,
    confidence REAL,
    confirmed INTEGER,
    is_duplicate INTEGER DEFAULT 0,
    is_reprint INTEGER DEFAULT 0,
    operator_notes TEXT,
    
    FOREIGN KEY (matched_unit_id) REFERENCES units(id),
    FOREIGN KEY (employee_number) REFERENCES users(employee_number)
);

CREATE INDEX IF NOT EXISTS idx_scan_history_date ON scan_history(scanned_at);
CREATE INDEX IF NOT EXISTS idx_scan_history_unit ON scan_history(matched_unit_id);

-- ============================================================================
-- ABS cache table: cached data from ABS API
-- ============================================================================
CREATE TABLE IF NOT EXISTS abs_cache (
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
    is_stale INTEGER DEFAULT 0
);

-- ============================================================================
-- Extraction queue table: tracks pending STL processing
-- ============================================================================
CREATE TABLE IF NOT EXISTS extraction_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stl_path TEXT NOT NULL UNIQUE,
    file_size_bytes INTEGER NOT NULL,
    file_created_at TIMESTAMP NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDING',
    error_message TEXT,
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    
    CHECK (status IN ('PENDING', 'IN_PROGRESS', 'COMPLETE', 'FAILED'))
);

CREATE INDEX IF NOT EXISTS idx_extraction_status ON extraction_queue(status);
CREATE INDEX IF NOT EXISTS idx_extraction_created ON extraction_queue(file_created_at DESC);

-- ============================================================================
-- Remilling flags table: suppresses duplicate alerts during remilling
-- ============================================================================
CREATE TABLE IF NOT EXISTS remilling_flags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL UNIQUE,
    flagged_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    flagged_by TEXT NOT NULL,
    notes TEXT
);

-- ============================================================================
-- Schema version tracking
-- ============================================================================
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

INSERT OR IGNORE INTO schema_version (version) VALUES (1);
