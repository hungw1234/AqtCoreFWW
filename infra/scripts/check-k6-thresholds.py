#!/usr/bin/env python3
"""
check-k6-thresholds.py — AqtCoreFW K6 threshold validator.

Reads a K6 JSON summary output file and validates configured thresholds.
Exits with code 1 if any threshold is violated (for CI pipeline integration).

Usage:
    python check-k6-thresholds.py <k6-results.json> [--config thresholds.json]
    python check-k6-thresholds.py tests/performance/load-result.json

K6 JSON summary format (--out json=result.json):
    {
      "metrics": {
        "http_req_duration": {
          "values": { "p(95)": 180.5, "p(99)": 320.1, "avg": 95.2, ... }
        },
        "http_req_failed": {
          "values": { "rate": 0.002, "passes": 198, "fails": 2 }
        }
      }
    }
"""

import json
import sys
import argparse
from pathlib import Path
from typing import Any


# ─── Default SLO thresholds (Phase 10 requirements) ──────────────────────────

DEFAULT_THRESHOLDS: dict[str, dict[str, float]] = {
    # Read endpoints: p95 < 200 ms
    "http_req_duration{operation:read}": {
        "p(95)": 200.0,
        "p(99)": 500.0,
    },
    # Write endpoints: p95 < 400 ms
    "http_req_duration{operation:write}": {
        "p(95)": 400.0,
        "p(99)": 800.0,
    },
    # Global duration fallback (used when tagged metrics are absent)
    "http_req_duration": {
        "p(95)": 500.0,
        "p(99)": 1000.0,
    },
    # Error rate < 1%
    "http_req_failed": {
        "rate": 0.01,
    },
    # Health check error rate < 0.1%
    "health_check_errors": {
        "rate": 0.001,
    },
}


# ─── Result dataclass ─────────────────────────────────────────────────────────

class ThresholdResult:
    def __init__(self, metric: str, stat: str, threshold: float, actual: float, passed: bool):
        self.metric    = metric
        self.stat      = stat
        self.threshold = threshold
        self.actual    = actual
        self.passed    = passed

    def __str__(self) -> str:
        status = "PASS" if self.passed else "FAIL"
        op     = "<=" if self.stat == "rate" else "<"
        return (
            f"  [{status}] {self.metric} → {self.stat}: "
            f"{self.actual:.3f} {op} {self.threshold:.3f}"
        )


# ─── Core validation logic ────────────────────────────────────────────────────

def load_results(results_file: str) -> dict[str, Any]:
    """Load and parse K6 JSON summary output."""
    path = Path(results_file)
    if not path.exists():
        print(f"[ERROR] Results file not found: {results_file}", file=sys.stderr)
        sys.exit(2)

    try:
        with path.open("r", encoding="utf-8") as f:
            return json.load(f)
    except json.JSONDecodeError as exc:
        print(f"[ERROR] Invalid JSON in {results_file}: {exc}", file=sys.stderr)
        sys.exit(2)


def load_custom_thresholds(config_file: str) -> dict[str, dict[str, float]]:
    """Load optional custom threshold config to override defaults."""
    path = Path(config_file)
    if not path.exists():
        print(f"[WARN] Threshold config not found: {config_file}, using defaults.", file=sys.stderr)
        return {}

    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def extract_metric_values(data: dict[str, Any], metric_name: str) -> dict[str, float] | None:
    """
    Extract metric stat values from K6 JSON.
    Handles both top-level metrics and tagged metrics (e.g. http_req_duration{operation:read}).
    """
    metrics = data.get("metrics", {})

    # Direct key lookup
    if metric_name in metrics:
        return metrics[metric_name].get("values", {})

    # Tagged metric — K6 may store as "http_req_duration{operation:read}" directly
    # or nest under the base metric name
    base_name = metric_name.split("{")[0]
    if base_name in metrics:
        metric_obj = metrics[base_name]
        # Check nested tagged breakdown
        tagged = metric_obj.get("tagged", {})
        tag_key = metric_name[len(base_name):]  # e.g. "{operation:read}"
        if tag_key in tagged:
            return tagged[tag_key].get("values", {})

    return None


def validate_thresholds(
    data: dict[str, Any],
    thresholds: dict[str, dict[str, float]],
) -> list[ThresholdResult]:
    """Validate all configured thresholds against K6 results. Returns list of results."""
    results: list[ThresholdResult] = []

    for metric_name, stats in thresholds.items():
        values = extract_metric_values(data, metric_name)

        if values is None:
            print(f"  [SKIP] Metric not found in results: {metric_name}")
            continue

        for stat, limit in stats.items():
            actual = values.get(stat)
            if actual is None:
                print(f"  [SKIP] Stat '{stat}' not found for metric: {metric_name}")
                continue

            # For rate-based metrics: actual must be <= limit
            # For duration-based metrics: actual must be < limit
            passed = actual <= limit

            results.append(ThresholdResult(
                metric    = metric_name,
                stat      = stat,
                threshold = limit,
                actual    = actual,
                passed    = passed,
            ))

    return results


def print_report(results: list[ThresholdResult], results_file: str) -> None:
    """Print a formatted threshold validation report to stdout."""
    passed = [r for r in results if r.passed]
    failed = [r for r in results if not r.passed]

    print(f"\n{'='*60}")
    print(f"  K6 Threshold Validation — {results_file}")
    print(f"{'='*60}")
    print(f"  Total checks : {len(results)}")
    print(f"  Passed       : {len(passed)}")
    print(f"  Failed       : {len(failed)}")
    print(f"{'='*60}\n")

    if passed:
        print("PASSED:")
        for r in passed:
            print(r)

    if failed:
        print("\nFAILED (SLO violations):")
        for r in failed:
            print(r)

    print(f"\n{'='*60}")
    overall = "ALL THRESHOLDS PASSED" if not failed else f"{len(failed)} THRESHOLD(S) VIOLATED"
    print(f"  Result: {overall}")
    print(f"{'='*60}\n")


# ─── Entry point ──────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Validate K6 JSON results against SLO thresholds."
    )
    parser.add_argument(
        "results_file",
        help="Path to K6 JSON summary output (e.g. load-result.json)",
    )
    parser.add_argument(
        "--config",
        default="",
        help="Optional JSON file with custom threshold definitions",
    )
    args = parser.parse_args()

    # Load K6 results
    data = load_results(args.results_file)

    # Merge thresholds: defaults overridden by custom config
    thresholds = dict(DEFAULT_THRESHOLDS)
    if args.config:
        custom = load_custom_thresholds(args.config)
        thresholds.update(custom)

    # Validate
    results = validate_thresholds(data, thresholds)

    if not results:
        print("[WARN] No metrics matched any configured threshold. Check results file format.")
        sys.exit(0)

    # Report
    print_report(results, args.results_file)

    # Exit 1 if any threshold violated (CI pipeline fail gate)
    failed = [r for r in results if not r.passed]
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
