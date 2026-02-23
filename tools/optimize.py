#!/usr/bin/env python3
"""
AUM — Parallel Bayesian Parameter Optimization via Optuna
==========================================================

Tunes C++ extraction parameters by running the C# headless pipeline
for each trial and maximizing correct-match scores.

Parallelism:
  Runs 6 concurrent workers on i9-14th gen (24 cores / 32 threads).
  Each worker gets OMP_NUM_THREADS=4 so PCL/FAISS don't fight for cores.
  6 workers × 4 OMP threads = 24 cores fully utilized.
  Memory: ~1GB per worker → ~6GB total (64GB available).

Ground Truth Convention:
  The correct Case ID is embedded in the query filename.
  We strip prefixes (SCAN_, QUERY_) and suffixes (_cut, _partial, _full)
  then match against the result's MatchedCase.

Usage:
  pip install optuna
  python optimize.py
"""

import json
import os
import re
import subprocess
import sys
import tempfile
import threading
from pathlib import Path

import optuna

# ============================================================================
# CONFIGURATION
# ============================================================================

DB_DIR = r"C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Tuning_DB"
QUERY_DIR = r"C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Physical_Queries"
AUM_EXE = r"C:\Users\Joseph.Lucio\OneDrive - The Dental Alliance\Desktop\Programs\AUM\Auto_Unit_Matcher\src\AUM.UI\bin\Release\net8.0-windows\AUM.exe"

WORK_DIR = Path(__file__).parent
STUDY_NAME = "aum_parameter_tuning"
STUDY_DB = f"sqlite:///{WORK_DIR / 'optuna_study.db'}"

# ── Parallelism settings (i9-14th gen: 8P + 16E = 24C / 32T, 64GB RAM) ──
N_TRIALS = 50              # Total trials across ALL workers
N_WORKERS = 3              # Concurrent trial workers (1100 DB units = heavy per trial)
OMP_THREADS_PER_WORKER = 8 # OpenMP threads per AUM.exe subprocess
TIMEOUT_SECONDS = 3600     # 1-hour timeout (1100 units + 100 queries = ~20-40min per trial)

# Thread-local storage for per-worker temp files
_local = threading.local()


# ============================================================================
# GROUND TRUTH
# ============================================================================

def normalize_name(name: str) -> str:
    """
    Normalize a filename for comparison.
    DB files use spaces:      '2025-118910 B1_0'
    Query files use underscores: '2025-118910_B1_0'
    
    Normalizes by lowercasing and replacing all separators (space, underscore,
    hyphen sequences) with a single canonical separator.
    """
    # Lowercase
    n = name.lower().strip()
    # Remove file extension if present
    n = Path(n).stem
    # Replace all whitespace and underscores with a single space
    n = re.sub(r'[\s_]+', ' ', n)
    return n


# ============================================================================
# OBJECTIVE FUNCTION (called by each worker thread)
# ============================================================================

def objective(trial: optuna.Trial) -> float:
    """
    1. Suggest parameters
    2. Write worker-specific config JSON
    3. Run AUM.exe --tune with OMP_NUM_THREADS limited
    4. Read worker-specific results JSON
    5. Score: sum of Final Scores for correct matches only
    """

    # Per-worker temp files (avoid collisions between parallel workers)
    worker_id = threading.current_thread().name
    config_path = WORK_DIR / f"tune_config_{trial.number}.json"
    results_path = WORK_DIR / f"results_{trial.number}.json"

    # Step 1: Suggest parameters
    voxel_size = trial.suggest_float("VoxelSize", 0.1, 0.3)
    keypoint_voxel = trial.suggest_float("KeypointVoxelSize", 0.3, 1.0)
    shot_radius = trial.suggest_float("ShotRadius", 0.5, 2.0)
    icp_decay = trial.suggest_float("IcpFitnessDecay", 0.1, 1.0)

    config = {
        "VoxelSize": round(voxel_size, 4),
        "KeypointVoxelSize": round(keypoint_voxel, 4),
        "ShotRadius": round(shot_radius, 4),
        "IcpFitnessDecay": round(icp_decay, 4),
    }

    print(
        f"\n[Worker {worker_id}] Trial #{trial.number}  "
        f"V={config['VoxelSize']:.3f}  K={config['KeypointVoxelSize']:.3f}  "
        f"S={config['ShotRadius']:.3f}  D={config['IcpFitnessDecay']:.3f}"
    )

    # Step 2: Write config
    try:
        with open(config_path, "w") as f:
            json.dump(config, f, indent=2)
    except IOError as e:
        print(f"  [Trial {trial.number}] ❌ Failed to write config: {e}")
        return 0.0

    # Step 3: Run headless C# app with throttled thread count
    cmd = [
        str(AUM_EXE),
        "--tune",
        "--config", str(config_path),
        "--db-folder", DB_DIR,
        "--query-folder", QUERY_DIR,
        "--out", str(results_path),
    ]

    env = os.environ.copy()
    env["OMP_NUM_THREADS"] = str(OMP_THREADS_PER_WORKER)

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=TIMEOUT_SECONDS,
            env=env,
        )

        if result.returncode != 0:
            print(f"  [Trial {trial.number}] ❌ Exit code {result.returncode}")
            if result.stderr:
                # Show first 300 chars of stderr
                print(f"  stderr: {result.stderr[:300]}")
            return 0.0

    except subprocess.TimeoutExpired:
        print(f"  [Trial {trial.number}] ❌ Timed out (>{TIMEOUT_SECONDS}s)")
        return 0.0
    except FileNotFoundError:
        print(f"  [Trial {trial.number}] ❌ AUM.exe not found")
        return 0.0
    finally:
        # Clean up config file
        config_path.unlink(missing_ok=True)

    # Step 4: Read results
    if not results_path.exists():
        print(f"  [Trial {trial.number}] ❌ No results.json produced")
        return 0.0

    try:
        with open(results_path, "r") as f:
            results = json.load(f)
    except (json.JSONDecodeError, IOError) as e:
        print(f"  [Trial {trial.number}] ❌ Bad results.json: {e}")
        return 0.0
    finally:
        results_path.unlink(missing_ok=True)

    # Step 5: Score — only correct matches contribute
    total_score = 0.0
    correct = 0
    wrong = 0

    for entry in results:
        query = entry.get("Query", "")
        matched = entry.get("MatchedCase", "")
        score = entry.get("Score", 0.0)

        # Normalize both: spaces vs underscores, case-insensitive
        norm_query = normalize_name(query)
        norm_matched = normalize_name(matched)

        if norm_query == norm_matched:
            total_score += score
            correct += 1
        else:
            wrong += 1

    print(
        f"  [Trial {trial.number}] Score: {total_score:.1f}  "
        f"✅ {correct}/{correct + wrong} correct"
    )

    trial.set_user_attr("correct", correct)
    trial.set_user_attr("wrong", wrong)
    trial.set_user_attr("total_queries", correct + wrong)

    return total_score


# ============================================================================
# MAIN
# ============================================================================

def main():
    print("=" * 60)
    print("AUM — Parallel Bayesian Parameter Optimization")
    print("=" * 60)
    print(f"  DB folder:     {DB_DIR}")
    print(f"  Query folder:  {QUERY_DIR}")
    print(f"  AUM.exe:       {AUM_EXE}")
    print(f"  Total trials:  {N_TRIALS}")
    print(f"  Workers:       {N_WORKERS} (× {OMP_THREADS_PER_WORKER} OMP threads = {N_WORKERS * OMP_THREADS_PER_WORKER} total)")
    print(f"  CPU budget:    {N_WORKERS * OMP_THREADS_PER_WORKER} threads")
    print(f"  Study DB:      {STUDY_DB}")
    print()

    # Validate paths
    errors = []
    if not os.path.isdir(DB_DIR):
        errors.append(f"DB folder not found: {DB_DIR}")
    if not os.path.isdir(QUERY_DIR):
        errors.append(f"Query folder not found: {QUERY_DIR}")
    if not os.path.isfile(AUM_EXE):
        errors.append(f"AUM.exe not found: {AUM_EXE}")

    if errors:
        for e in errors:
            print(f"❌ {e}")
        sys.exit(1)

    # Create or load persistent study
    study = optuna.create_study(
        study_name=STUDY_NAME,
        storage=STUDY_DB,
        direction="maximize",
        load_if_exists=True,
    )

    completed = len([t for t in study.trials if t.state == optuna.trial.TrialState.COMPLETE])
    if completed:
        print(f"  Resuming study with {completed} completed trials")
        print(f"  Current best: {study.best_value:.1f} (Trial #{study.best_trial.number})")

    print(f"\nStarting {N_TRIALS} trials across {N_WORKERS} parallel workers...\n")

    # ── Run with parallel workers ──
    # Optuna's n_jobs parameter uses threading internally.
    # Each thread spawns an AUM.exe subprocess, so real parallelism
    # comes from the OS process scheduler, not the GIL.
    study.optimize(
        objective,
        n_trials=N_TRIALS,
        n_jobs=N_WORKERS,
        show_progress_bar=True,
    )

    # ── Print final results ──
    print("\n" + "=" * 60)
    print("OPTIMIZATION COMPLETE")
    print("=" * 60)

    print(f"\nBest trial: #{study.best_trial.number}")
    print(f"Best score: {study.best_value:.1f}")
    print(f"\nBest parameters:")
    for key, value in study.best_params.items():
        print(f"  {key}: {value:.4f}")

    # Save best params
    best_path = WORK_DIR / "best_params.json"
    with open(best_path, "w") as f:
        json.dump(study.best_params, f, indent=2)
    print(f"\nBest params saved to: {best_path}")

    # Leaderboard
    complete_trials = [
        t for t in study.trials
        if t.state == optuna.trial.TrialState.COMPLETE and t.value is not None
    ]
    print(f"\nLeaderboard ({len(complete_trials)} trials):")
    print(f"{'#':>5} | {'Score':>8} | {'Correct':>7} | {'VoxelSz':>8} | {'KpVoxel':>8} | {'ShotR':>8} | {'Decay':>8}")
    print("-" * 72)

    for t in sorted(complete_trials, key=lambda x: x.value, reverse=True)[:20]:
        correct = t.user_attrs.get("correct", "?")
        total = t.user_attrs.get("total_queries", "?")
        print(
            f"{t.number:5d} | {t.value:8.1f} | {correct:>3}/{total:<3} | "
            f"{t.params.get('VoxelSize', 0):8.4f} | "
            f"{t.params.get('KeypointVoxelSize', 0):8.4f} | "
            f"{t.params.get('ShotRadius', 0):8.4f} | "
            f"{t.params.get('IcpFitnessDecay', 0):8.4f}"
        )


if __name__ == "__main__":
    main()
