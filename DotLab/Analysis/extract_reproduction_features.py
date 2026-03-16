"""
extract_reproduction_features.py

Extracts the key observed features that are strongly correlated with P from
existing kernel sweep CSV outputs and writes them to a new reproduction-
features CSV.

Primary source: *-stair-prediction-compare.csv  (obs_* columns)
Fallback source: *-stair-summary.csv            (for files that pre-date the
                                                  joint model additions, or
                                                  when no compare CSV exists)

Output columns
--------------
p_header, p_value,
joint_count, joint_step, joint_span,
local_slope_u250, local_slope_u400, local_slope_u550,
local_slope_u700, local_slope_u850,
curvature_budget, terminal_headroom

Usage
-----
  # auto-detect: processes ALL *-stair-prediction-compare.csv files under
  # DotLab/Kernel, plus any *-stair-summary.csv that has no companion compare
  python extract_reproduction_features.py

  # single compare CSV (summary inferred automatically from sibling)
  python extract_reproduction_features.py <compare_csv>

  # explicit compare + summary
  python extract_reproduction_features.py <compare_csv> <summary_csv>

  # explicit compare + summary + output destination
  python extract_reproduction_features.py <compare_csv> <summary_csv> <output_csv>

  # summary-only (no compare CSV available)
  python extract_reproduction_features.py --summary <summary_csv>
  python extract_reproduction_features.py --summary <summary_csv> <output_csv>
"""

import csv
import os
import sys


_SLOPE_ANCHORS = ("u250", "u400", "u550", "u700", "u850")

# Features extracted directly from obs_* columns in the compare CSV
_OBS_FEATURES = (
    "joint_count",
    "joint_step",
    "joint_span",
    *(f"local_slope_{a}" for a in _SLOPE_ANCHORS),
    "curvature_budget",
    "terminal_headroom",
)

_OUTPUT_COLUMNS = ("p_header", "p_value") + _OBS_FEATURES


def _read_csv_rows(path):
    """Read a CSV, skipping comment lines that start with '#'."""
    with open(path, newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(
            (line for line in f if not line.lstrip().startswith("#"))
        )
        return list(reader)


def _resolve_sibling(base_path, suffix, replacement_suffix):
    """Given base_path ending with suffix, return the sibling path ending with replacement_suffix."""
    if base_path.endswith(suffix):
        return base_path[: -len(suffix)] + replacement_suffix
    return None


def _auto_detect_compare(kernel_dir):
    """Return all *-stair-prediction-compare.csv paths found under kernel_dir."""
    found = []
    for root, _dirs, files in os.walk(kernel_dir):
        for name in sorted(files):
            if name.endswith("-stair-prediction-compare.csv"):
                found.append(os.path.join(root, name))
    return found


def _auto_detect_summary_only(kernel_dir, compare_paths):
    """Return *-stair-summary.csv paths that have no companion compare CSV."""
    compare_bases = set()
    for cp in compare_paths:
        base = _resolve_sibling(cp, "-stair-prediction-compare.csv", "-stair-summary.csv")
        if base:
            compare_bases.add(os.path.normpath(base))

    summary_only = []
    for root, _dirs, files in os.walk(kernel_dir):
        for name in sorted(files):
            if name.endswith("-stair-summary.csv"):
                full = os.path.normpath(os.path.join(root, name))
                if full not in compare_bases:
                    summary_only.append(os.path.join(root, name))
    return summary_only


def extract(compare_path, summary_path=None, output_path=None):
    """Extract reproduction features and write the output CSV.

    Parameters
    ----------
    compare_path : str
        Path to a *-stair-prediction-compare.csv.
    summary_path : str or None
        Path to the matching *-stair-summary.csv.  When None the path is
        inferred automatically from compare_path.
    output_path : str or None
        Destination CSV path.  When None the path is derived from compare_path
        by replacing the suffix with '-reproduction-features.csv'.
    """

    if not os.path.isfile(compare_path):
        raise FileNotFoundError(f"compare CSV not found: {compare_path}")

    # --- resolve sibling summary path ----------------------------------------
    if summary_path is None:
        summary_path = _resolve_sibling(
            compare_path,
            "-stair-prediction-compare.csv",
            "-stair-summary.csv",
        )

    # --- resolve output path -------------------------------------------------
    if output_path is None:
        output_path = _resolve_sibling(
            compare_path,
            "-stair-prediction-compare.csv",
            "-reproduction-features.csv",
        )
        if output_path is None:
            output_path = compare_path.replace(".csv", "-reproduction-features.csv")

    # --- load compare CSV ----------------------------------------------------
    compare_rows = _read_csv_rows(compare_path)
    if not compare_rows:
        raise ValueError(f"compare CSV contains no data rows: {compare_path}")

    compare_header = set(compare_rows[0].keys())
    has_obs = {feat: f"obs_{feat}" in compare_header for feat in _OBS_FEATURES}

    # --- load summary CSV (fallback) -----------------------------------------
    summary_by_p = {}
    if summary_path and os.path.isfile(summary_path):
        for row in _read_csv_rows(summary_path):
            key = row.get("p_header", "").strip()
            if key:
                summary_by_p[key] = row

    # --- build output rows ---------------------------------------------------
    out_rows = []
    for crow in compare_rows:
        p_header = crow.get("p_header", "").strip()
        p_value = crow.get("p_value", "").strip()
        srow = summary_by_p.get(p_header, {})

        record = {"p_header": p_header, "p_value": p_value}

        for feat in _OBS_FEATURES:
            obs_col = f"obs_{feat}"
            if has_obs.get(feat):
                record[feat] = crow.get(obs_col, "").strip()
            else:
                record[feat] = _fallback_value(feat, p_value, srow)

        out_rows.append(record)

    # --- write output CSV ----------------------------------------------------
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    with open(output_path, "w", newline="", encoding="utf-8") as f:
        f.write("# source=reproduction features\n")
        f.write(f"# input_file={os.path.basename(compare_path)}\n")
        writer = csv.DictWriter(f, fieldnames=_OUTPUT_COLUMNS)
        writer.writeheader()
        writer.writerows(out_rows)

    print(f"wrote {len(out_rows)} rows -> {output_path}")
    return output_path


def _fallback_value(feat, p_value_str, summary_row):
    """Compute a feature value from stair-summary columns when the compare CSV
    does not carry the corresponding obs_* column."""
    if not summary_row:
        return ""

    if feat == "joint_count":
        return summary_row.get("plateau_count", "")

    if feat == "joint_step":
        try:
            p = float(p_value_str)
            mean_riser = float(summary_row.get("mean_riser01", ""))
            return str(mean_riser / p) if p != 0 else ""
        except (ValueError, ZeroDivisionError):
            return ""

    if feat == "joint_span":
        return summary_row.get("median_tread_px", "")

    if feat == "terminal_headroom":
        try:
            last_norm = float(summary_row.get("last_nonzero_r_norm", ""))
            return str(100.0 - last_norm)
        except ValueError:
            return ""

    # local_slope_u* and curvature_budget have no simple summary-based fallback
    return ""


def extract_from_summary(summary_path, output_path=None):
    """Extract reproduction features from a stair-summary CSV alone.

    This is used when no companion *-stair-prediction-compare.csv exists.
    local_slope_u* and curvature_budget will be empty because they can only be
    derived from the compare CSV.

    Parameters
    ----------
    summary_path : str
        Path to a *-stair-summary.csv.
    output_path : str or None
        Destination CSV path.  When None the path is derived from summary_path
        by replacing '-stair-summary.csv' with '-reproduction-features.csv'.
    """

    if not os.path.isfile(summary_path):
        raise FileNotFoundError(f"summary CSV not found: {summary_path}")

    if output_path is None:
        output_path = _resolve_sibling(
            summary_path,
            "-stair-summary.csv",
            "-reproduction-features.csv",
        )
        if output_path is None:
            output_path = summary_path.replace(".csv", "-reproduction-features.csv")

    summary_rows = _read_csv_rows(summary_path)
    if not summary_rows:
        raise ValueError(f"summary CSV contains no data rows: {summary_path}")

    out_rows = []
    for srow in summary_rows:
        p_header = srow.get("p_header", "").strip()
        p_value = srow.get("p_value", "").strip()

        record = {"p_header": p_header, "p_value": p_value}
        for feat in _OBS_FEATURES:
            record[feat] = _fallback_value(feat, p_value, srow)
        out_rows.append(record)

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    with open(output_path, "w", newline="", encoding="utf-8") as f:
        f.write("# source=reproduction features\n")
        f.write(f"# input_file={os.path.basename(summary_path)}\n")
        writer = csv.DictWriter(f, fieldnames=_OUTPUT_COLUMNS)
        writer.writeheader()
        writer.writerows(out_rows)

    print(f"wrote {len(out_rows)} rows -> {output_path}")
    return output_path


def main():
    args = sys.argv[1:]

    # --summary mode: extract from summary CSV only (no compare CSV)
    if args and args[0] == "--summary":
        rest = args[1:]
        if not rest:
            print("Usage: extract_reproduction_features.py --summary <summary_csv> [output_csv]")
            sys.exit(1)
        summary_csv = rest[0]
        out_csv = rest[1] if len(rest) >= 2 else None
        extract_from_summary(summary_csv, output_path=out_csv)
        return

    if len(args) == 0:
        # auto-detect: process every compare CSV under DotLab/Kernel, plus any
        # summary CSVs that have no companion compare CSV
        script_dir = os.path.dirname(os.path.abspath(__file__))
        kernel_dir = os.path.normpath(os.path.join(script_dir, "..", "Kernel"))

        compare_paths = _auto_detect_compare(kernel_dir)
        summary_only_paths = _auto_detect_summary_only(kernel_dir, compare_paths)

        if not compare_paths and not summary_only_paths:
            print("No target CSVs found under", kernel_dir)
            sys.exit(1)

        for cp in compare_paths:
            print(f"processing compare: {cp}")
            extract(cp)

        for sp in summary_only_paths:
            print(f"processing summary-only: {sp}")
            extract_from_summary(sp)

    elif len(args) == 1:
        extract(args[0])

    elif len(args) == 2:
        extract(args[0], summary_path=args[1])

    else:
        extract(args[0], summary_path=args[1], output_path=args[2])


if __name__ == "__main__":
    main()
