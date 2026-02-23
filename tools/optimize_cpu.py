#!/usr/bin/env python3
"""
optimize_cpu.py — Pure-Python Optuna tuning via ctypes into AUM.Engine.dll.

Native Windows ctypes approach:
  - DLL loaded once per PROCESS (each worker = separate process = isolated DLL)
  - FAISS voting + RANSAC + ICP run inside the native C++ engine
  - Early exit at >90% match keeps ICP fast (~1-2 candidates/query)

Multi-process parallelism:
  - N_WORKERS separate processes, each with its own DLL instance
  - Optuna coordinates trials via SQLite (safe for multi-process)
  - 6 workers × 4 OMP threads = 24 threads = saturates i9-14th gen

Usage:
    python tools/optimize_cpu.py                  # 6 workers (default)
    python tools/optimize_cpu.py --workers 4      # custom worker count
    python tools/optimize_cpu.py --worker-id 0    # internal: single worker mode
"""
from __future__ import annotations

import argparse
import ctypes
import json
import os
import re
import subprocess
import sys
import time
from ctypes import (
    POINTER, Structure, c_char_p, c_float, c_int, c_int64, c_size_t,
    c_uint8, c_void_p, byref,
)
from pathlib import Path

import numpy as np
import optuna

# Suppress PCL stderr noise (SHOT local reference frame warnings)
os.environ["PCL_VERBOSITY_LEVEL"] = "L_ERROR"

# ============================================================================
# PATHS — adjust if your layout differs
# ============================================================================
PROJECT_ROOT = Path(__file__).resolve().parent.parent
ENGINE_DIR = PROJECT_ROOT / "src" / "AUM.UI" / "bin" / "Release" / "net8.0-windows"
ENGINE_DLL = ENGINE_DIR / "AUM.Engine.dll"

DB_FOLDER = Path(r"C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Tuning_DB")
QUERY_FOLDER = Path(r"C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Physical_Queries")
WORK_DIR = PROJECT_ROOT / "tools"
STUDY_DB = f"sqlite:///{WORK_DIR / 'optuna_cpu_study.db'}"
STUDY_NAME = "aum_cpu_tuning_v1"

# ── Tuning settings ──
N_TRIALS = 50              # Total trials across ALL workers
N_WORKERS = 8              # Increased from 2 to 8 to improve overall CPU utilization
OMP_THREADS = 3            # Reduced to 3 to prevent thread thrashing (8x3=24 active threads)
TOP_K = 10                 # Stage 1 vote candidates for Stage 2 verification

# ============================================================================
# C STRUCT MIRRORS
# ============================================================================

class AUM_DescriptorConfig(Structure):
    _fields_ = [
        ("voxelSize", c_float),
        ("keypointVoxelSize", c_float),
        ("shotRadius", c_float),
        ("icpFitnessDecay", c_float),
    ]

class AUM_VoteResult(Structure):
    _fields_ = [
        ("unitId", c_int64),
        ("voteScore", c_float),
        ("voteCount", c_int),
    ]

class AUM_VerificationResult(Structure):
    _fields_ = [
        ("unitId", c_int64),
        ("ransacInlierRatio", c_float),
        ("icpFitnessScore", c_float),
        ("finalScore", c_float),
        ("ransacInliers", c_int),
        ("correspondences", c_int),
    ]


# ============================================================================
# ENGINE WRAPPER
# ============================================================================

class AUMEngine:
    """Thin ctypes wrapper around AUM.Engine.dll."""

    AUM_SUCCESS = 0

    def __init__(self, dll_path: Path):
        dll_dir = str(dll_path.parent)
        os.add_dll_directory(dll_dir)
        os.environ["PATH"] = dll_dir + ";" + os.environ.get("PATH", "")

        self._lib = ctypes.CDLL(str(dll_path))
        self._setup_signatures()

    def _setup_signatures(self):
        L = self._lib

        L.aum_get_version.restype = c_char_p
        L.aum_get_version.argtypes = []
        L.aum_get_last_error.restype = c_char_p
        L.aum_get_last_error.argtypes = []

        # Config
        L.aum_set_config.restype = c_int
        L.aum_set_config.argtypes = [POINTER(AUM_DescriptorConfig)]

        # Parse STL
        L.aum_parse_stl.restype = c_int
        L.aum_parse_stl.argtypes = [c_char_p, POINTER(c_void_p)]
        L.aum_free_point_cloud.restype = None
        L.aum_free_point_cloud.argtypes = [c_void_p]

        # Extract descriptors
        L.aum_extract_descriptors.restype = c_int
        L.aum_extract_descriptors.argtypes = [c_void_p, POINTER(c_void_p)]
        L.aum_free_descriptor.restype = None
        L.aum_free_descriptor.argtypes = [c_void_p]

        # Serialize/deserialize
        L.aum_serialize_descriptor.restype = c_int
        L.aum_serialize_descriptor.argtypes = [c_void_p, POINTER(POINTER(c_uint8)), POINTER(c_size_t)]
        L.aum_deserialize_descriptor.restype = c_int
        L.aum_deserialize_descriptor.argtypes = [POINTER(c_uint8), c_size_t, POINTER(c_void_p)]
        L.aum_free_blob.restype = None
        L.aum_free_blob.argtypes = [POINTER(c_uint8)]

        # Index
        L.aum_create_index.restype = c_int
        L.aum_create_index.argtypes = [POINTER(c_void_p)]
        L.aum_add_to_index.restype = c_int
        L.aum_add_to_index.argtypes = [c_void_p, c_void_p, c_int64]
        L.aum_train_index.restype = c_int
        L.aum_train_index.argtypes = [c_void_p]
        L.aum_free_index.restype = None
        L.aum_free_index.argtypes = [c_void_p]

        # Query votes
        L.aum_query_votes.restype = c_int
        L.aum_query_votes.argtypes = [
            c_void_p, c_void_p, c_int, POINTER(AUM_VoteResult), POINTER(c_int),
        ]

        # Verify
        L.aum_verify.restype = c_int
        L.aum_verify.argtypes = [c_void_p, c_void_p, POINTER(AUM_VerificationResult)]
        L.aum_verify_with_decay.restype = c_int
        L.aum_verify_with_decay.argtypes = [c_void_p, c_void_p, c_float, POINTER(AUM_VerificationResult)]

    def _check(self, code: int, context: str = ""):
        if code != self.AUM_SUCCESS:
            err = self._lib.aum_get_last_error()
            msg = err.decode("utf-8", errors="replace") if err else "unknown"
            raise RuntimeError(f"AUM error ({code}) in {context}: {msg}")

    def get_version(self) -> str:
        v = self._lib.aum_get_version()
        return v.decode("utf-8") if v else "?"

    def set_config(self, voxel: float, kp_voxel: float, shot_r: float, icp_decay: float):
        cfg = AUM_DescriptorConfig(
            voxelSize=voxel, keypointVoxelSize=kp_voxel,
            shotRadius=shot_r, icpFitnessDecay=icp_decay,
        )
        self._check(self._lib.aum_set_config(byref(cfg)), "set_config")

    def parse_stl(self, path: str) -> c_void_p:
        handle = c_void_p()
        self._check(self._lib.aum_parse_stl(path.encode("utf-8"), byref(handle)),
                     f"parse_stl({Path(path).name})")
        return handle

    def free_point_cloud(self, handle):
        self._lib.aum_free_point_cloud(handle)

    def extract_descriptors(self, pc_handle) -> c_void_p:
        desc = c_void_p()
        self._check(self._lib.aum_extract_descriptors(pc_handle, byref(desc)), "extract")
        return desc

    def free_descriptor(self, handle):
        self._lib.aum_free_descriptor(handle)

    def create_index(self) -> c_void_p:
        handle = c_void_p()
        self._check(self._lib.aum_create_index(byref(handle)), "create_index")
        return handle

    def add_to_index(self, idx, desc, unit_id: int):
        self._check(self._lib.aum_add_to_index(idx, desc, c_int64(unit_id)), "add_to_index")

    def train_index(self, idx):
        self._check(self._lib.aum_train_index(idx), "train_index")

    def free_index(self, idx):
        self._lib.aum_free_index(idx)

    def query_votes(self, idx, query_desc, top_k: int) -> list[AUM_VoteResult]:
        results = (AUM_VoteResult * top_k)()
        count = c_int(0)
        self._check(self._lib.aum_query_votes(idx, query_desc, top_k, results, byref(count)),
                     "query_votes")
        return [results[i] for i in range(count.value)]

    def verify_with_decay(self, query_desc, cand_desc, decay: float) -> AUM_VerificationResult:
        result = AUM_VerificationResult()
        self._check(self._lib.aum_verify_with_decay(
            query_desc, cand_desc, c_float(decay), byref(result)), "verify")
        return result

    def extract_from_stl(self, stl_path: str):
        """Parse STL → extract descriptor → free point cloud. Returns descriptor handle."""
        pc = self.parse_stl(stl_path)
        try:
            desc = self.extract_descriptors(pc)
        finally:
            self.free_point_cloud(pc)
        return desc


# ============================================================================
# C&B FILTER
# ============================================================================

_EXCLUSION_KW = ["waxup", "wax-up", "denture", "base", "abutment", "model", "splint"]

def is_cb_file(path: str) -> bool:
    name = Path(path).name.lower()
    parent = Path(path).parent.name.lower()
    return not any(kw in name or kw in parent for kw in _EXCLUSION_KW)


# ============================================================================
# GROUND TRUTH NORMALIZATION
# ============================================================================

def normalize_name(name: str) -> str:
    """DB uses spaces, queries use underscores — normalize both."""
    n = Path(name).stem.lower().strip()
    return re.sub(r'[\s_]+', ' ', n)


# ============================================================================
# LIVE STATUS (written to JSON for dashboard)
# ============================================================================

LIVE_DIR = WORK_DIR / "charts"

def _write_live_status(worker_id: int, status: dict):
    """Write worker's live progress to JSON for the dashboard."""
    try:
        LIVE_DIR.mkdir(parents=True, exist_ok=True)
        path = LIVE_DIR / f"live_w{worker_id}.json"
        status["timestamp"] = time.strftime("%Y-%m-%d %H:%M:%S")
        # Write to temp then rename for atomicity
        tmp = path.with_suffix(".tmp")
        with open(tmp, "w") as f:
            json.dump(status, f)
        tmp.replace(path)
    except Exception:
        pass  # Never crash the trial over status writing


# ============================================================================
# SINGLE TRIAL (runs inside a WORKER PROCESS)
# ============================================================================

def run_trial(engine: AUMEngine, trial: optuna.Trial, worker_id: int) -> float:
    """
    One Optuna trial:
      1. Set extraction config
      2. Extract descriptors for all DB units
      3. Build FAISS index
      4. Match all queries (Stage 1 + Stage 2 with early exit)
      5. Score by correct-match-weighted ICP score
    """
    tag = f"[W{worker_id}:T{trial.number}]"

    # Sample parameters (narrowed to gold-mine basin found in first 18 trials)
    voxel = trial.suggest_float("VoxelSize", 0.15, 0.35)
    # Force keypoints to be at least 0.15mm sparser than base voxel (prevents bloat)
    kp_voxel = trial.suggest_float("KeypointVoxelSize", voxel + 0.15, 0.8)
    shot_r = trial.suggest_float("ShotRadius", 1.2, 2.5)
    icp_decay = trial.suggest_float("IcpFitnessDecay", 0.3, 0.9)

    params = {"VoxelSize": round(voxel, 4), "KpVoxelSize": round(kp_voxel, 4),
              "ShotRadius": round(shot_r, 4), "IcpDecay": round(icp_decay, 4)}

    print(f"\n{tag} V={voxel:.3f}  K={kp_voxel:.3f}  S={shot_r:.3f}  D={icp_decay:.3f}",
          flush=True)

    _write_live_status(worker_id, {
        "worker": worker_id, "trial": trial.number, "phase": "extracting",
        "params": params, "extraction": {"done": 0, "total": 0},
        "matching": {"done": 0, "total": 0, "correct": 0, "accuracy_curve": []},
    })

    engine.set_config(voxel, kp_voxel, shot_r, icp_decay)

    # ── Stage 0: Extract all DB descriptors ──
    db_files = sorted([str(f) for f in DB_FOLDER.glob("*.stl") if is_cb_file(str(f))])
    if not db_files:
        print(f"  {tag} No DB files found!", flush=True)
        return 0.0

    idx = engine.create_index()
    unit_map: dict[int, tuple[str, c_void_p]] = {}
    unit_id = 1
    t0 = time.time()
    n_total = len(db_files)
    n_errors = 0

    for i, stl_path in enumerate(db_files):
        try:
            desc = engine.extract_from_stl(stl_path)
            engine.add_to_index(idx, desc, unit_id)
            unit_map[unit_id] = (Path(stl_path).stem, desc)
            unit_id += 1
        except RuntimeError:
            n_errors += 1

        if (i + 1) % 50 == 0 or (i + 1) == n_total:
            elapsed = time.time() - t0
            rate = (i + 1) / elapsed if elapsed > 0 else 0
            eta = (n_total - i - 1) / rate if rate > 0 else 0
            print(f"  {tag} Extracting: {i+1}/{n_total} "
                  f"({elapsed:.0f}s elapsed, ETA {eta:.0f}s)", flush=True)
            _write_live_status(worker_id, {
                "worker": worker_id, "trial": trial.number, "phase": "extracting",
                "params": params,
                "extraction": {"done": i + 1, "total": n_total, "elapsed": round(elapsed)},
                "matching": {"done": 0, "total": 0, "correct": 0, "accuracy_curve": []},
            })

    if len(unit_map) < 2:
        engine.free_index(idx)
        return 0.0

    engine.train_index(idx)
    t_index = time.time() - t0
    print(f"  {tag} Indexed {len(unit_map)} units in {t_index:.0f}s "
          f"({n_errors} errors skipped)", flush=True)

    # ── Stage 1+2: Match queries ──
    query_files = sorted([str(f) for f in QUERY_FOLDER.glob("*.stl")])
    correct = 0
    wrong = 0
    total_score = 0.0
    t1 = time.time()
    n_queries = len(query_files)
    accuracy_curve = []  # (query_index, running_accuracy%)

    _write_live_status(worker_id, {
        "worker": worker_id, "trial": trial.number, "phase": "matching",
        "params": params,
        "extraction": {"done": n_total, "total": n_total, "elapsed": round(t_index)},
        "matching": {"done": 0, "total": n_queries, "correct": 0, "accuracy_curve": []},
    })

    for qi, qpath in enumerate(query_files):
        try:
            q_desc = engine.extract_from_stl(qpath)
            votes = engine.query_votes(idx, q_desc, TOP_K)

            best_score = 0.0
            best_case = "NO_MATCH"

            for vote in votes:
                uid = vote.unitId
                if uid not in unit_map:
                    continue
                case_id, cand_desc = unit_map[uid]
                try:
                    result = engine.verify_with_decay(q_desc, cand_desc, icp_decay)
                    if result.finalScore > best_score:
                        best_score = result.finalScore
                        best_case = case_id
                    if result.finalScore >= 90.0:
                        break  # Early exit
                except RuntimeError:
                    pass

            engine.free_descriptor(q_desc)

            # Ground truth
            if normalize_name(Path(qpath).name) == normalize_name(best_case):
                correct += 1
                total_score += best_score
            else:
                wrong += 1

        except RuntimeError:
            wrong += 1

        # Track accuracy curve
        done = qi + 1
        acc = round(100.0 * correct / done, 1)
        accuracy_curve.append([done, acc])

        if done % 10 == 0 or done == n_queries:
            elapsed = time.time() - t1
            print(f"  {tag} Matching: {done}/{n_queries} "
                  f"({correct} correct, {elapsed:.0f}s)", flush=True)

        # Write live status every 5 queries (balance between freshness and I/O)
        if done % 5 == 0 or done == n_queries:
            _write_live_status(worker_id, {
                "worker": worker_id, "trial": trial.number, "phase": "matching",
                "params": params,
                "extraction": {"done": n_total, "total": n_total, "elapsed": round(t_index)},
                "matching": {
                    "done": done, "total": n_queries,
                    "correct": correct, "wrong": wrong,
                    "elapsed": round(time.time() - t1),
                    "accuracy_curve": accuracy_curve,
                },
            })

    t_match = time.time() - t1

    # Cleanup
    for uid, (_, desc) in unit_map.items():
        engine.free_descriptor(desc)
    engine.free_index(idx)

    total = correct + wrong
    objective = total_score / total if total > 0 else 0.0
    print(f"  {tag} ✅ {correct}/{total} correct, "
          f"score={objective:.1f}, index={t_index:.0f}s, match={t_match:.0f}s", flush=True)

    return objective


# ============================================================================
# WORKER PROCESS ENTRY POINT
# ============================================================================

def run_worker(worker_id: int, n_trials: int):
    """Entry point for each worker process. Loads its own DLL, runs trials."""

    # Limit OpenMP threads per worker to avoid CPU oversubscription
    os.environ["OMP_NUM_THREADS"] = str(OMP_THREADS)
    os.environ["OMP_WAIT_POLICY"] = "PASSIVE"

    print(f"[Worker {worker_id}] Starting — {n_trials} trials, "
          f"{OMP_THREADS} OMP threads", flush=True)

    engine = AUMEngine(ENGINE_DLL)
    print(f"[Worker {worker_id}] Engine v{engine.get_version()}", flush=True)

    # Connect to shared Optuna study
    study = optuna.create_study(
        study_name=STUDY_NAME,
        storage=STUDY_DB,
        direction="maximize",
        load_if_exists=True,
    )

    study.optimize(
        lambda trial: run_trial(engine, trial, worker_id),
        n_trials=n_trials,
    )


# ============================================================================
# MAIN (ORCHESTRATOR)
# ============================================================================

def print_results():
    """Load the study and print the leaderboard."""
    study = optuna.create_study(
        study_name=STUDY_NAME,
        storage=STUDY_DB,
        direction="maximize",
        load_if_exists=True,
    )

    if not study.trials:
        print("No completed trials found.")
        return

    best = study.best_trial
    print("\n" + "=" * 70)
    print("OPTIMIZATION COMPLETE")
    print("=" * 70)
    print(f"\nBest trial: #{best.number}")
    print(f"Best score: {best.value:.1f}")
    print(f"\nBest parameters:")
    for k, v in best.params.items():
        print(f"  {k}: {v:.4f}")

    # Save best params
    out_path = WORK_DIR / "best_params.json"
    with open(out_path, "w") as f:
        json.dump(best.params, f, indent=2)
    print(f"\nSaved to: {out_path}")

    # Leaderboard
    completed = [t for t in study.trials if t.value is not None]
    print(f"\nLeaderboard ({len(completed)} completed trials):")
    print(f"{'#':>5} | {'Score':>8} | {'VoxelSz':>8} | {'KpVoxel':>8} | {'ShotR':>8} | {'Decay':>8}")
    print("-" * 70)
    for t in sorted(completed, key=lambda t: t.value, reverse=True)[:20]:
        p = t.params
        print(f"{t.number:>5} | {t.value:>8.1f} | {p.get('VoxelSize', 0):>8.4f} | "
              f"{p.get('KeypointVoxelSize', 0):>8.4f} | {p.get('ShotRadius', 0):>8.4f} | "
              f"{p.get('IcpFitnessDecay', 0):>8.4f}")


def main():
    parser = argparse.ArgumentParser(description="AUM CPU Optimizer")
    parser.add_argument("--workers", type=int, default=N_WORKERS,
                        help=f"Number of parallel worker processes (default: {N_WORKERS})")
    parser.add_argument("--trials", type=int, default=N_TRIALS,
                        help=f"Total trials to run (default: {N_TRIALS})")
    parser.add_argument("--worker-id", type=int, default=None,
                        help="Internal: run as a single worker with this ID")
    args = parser.parse_args()

    if not ENGINE_DLL.exists():
        print(f"ERROR: DLL not found at {ENGINE_DLL}")
        print("Run: dotnet build src/AUM.UI/AUM.UI.csproj -c Release")
        sys.exit(1)

    # ── Single worker mode (spawned by orchestrator) ──
    if args.worker_id is not None:
        # Each worker gets a fair share of trials
        trials_per = max(1, args.trials // args.workers)
        run_worker(args.worker_id, trials_per)
        return

    # ── Orchestrator mode (default) ──
    n_workers = args.workers
    total_trials = args.trials
    trials_per = max(1, total_trials // n_workers)

    print("=" * 70)
    print("AUM CPU OPTIMIZER — Multi-Process (ctypes + in-process engine)")
    print("=" * 70)
    print(f"\n  DB folder:     {DB_FOLDER}")
    print(f"  Query folder:  {QUERY_FOLDER}")
    print(f"  Total trials:  {total_trials}")
    print(f"  Workers:       {n_workers} processes × {OMP_THREADS} OMP threads = "
          f"{n_workers * OMP_THREADS} threads")
    print(f"  Trials/worker: {trials_per}")
    print(f"  Study DB:      {STUDY_DB}")
    print(f"\n  Estimated time: ~{trials_per * 9 // 60}h {trials_per * 9 % 60}m "
          f"(~9 min/trial)")
    print()

    # Suppress Optuna logs in orchestrator
    optuna.logging.set_verbosity(optuna.logging.WARNING)

    # Create study (so it exists before workers connect)
    optuna.create_study(
        study_name=STUDY_NAME,
        storage=STUDY_DB,
        direction="maximize",
        load_if_exists=True,
    )

    # Spawn worker processes
    script = str(Path(__file__).resolve())
    python = sys.executable
    processes = []

    for wid in range(n_workers):
        cmd = [
            python, script,
            "--worker-id", str(wid),
            "--workers", str(n_workers),
            "--trials", str(total_trials),
        ]
        env = os.environ.copy()
        env["OMP_NUM_THREADS"] = str(OMP_THREADS)
        env["OMP_WAIT_POLICY"] = "PASSIVE"
        env["PYTHONUNBUFFERED"] = "1"

        p = subprocess.Popen(cmd, env=env)
        processes.append(p)
        print(f"  Spawned worker {wid} (PID {p.pid})")

    # Launch dashboard auto-updater in the background
    chart_cmd = [sys.executable, str(WORK_DIR / "gen_charts.py"), "--watch"]
    chart_p = subprocess.Popen(chart_cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"  Spawned dashboard auto-updater (PID {chart_p.pid})")

    print(f"\n  All {n_workers} workers and dashboard running. Waiting for completion...\n")

    # Wait for all workers
    try:
        for p in processes:
            p.wait()
    except KeyboardInterrupt:
        print("\n  Optimization interrupted by user. Cleaning up...")
        for p in processes:
            p.terminate()
    finally:
        chart_p.terminate()

    # Print final results
    print_results()


if __name__ == "__main__":
    main()
