import csv
import os

path = os.path.join(os.path.dirname(__file__), "lineN1-vs-dotN1-match-20260211-203018.csv")
with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    h = next(r)
    row = next(r)

keys = [
    "best_th1_lut_loaded",
    "best_th1_alpha_l1_lut",
    "best_th1_over_area_lut",
    "best_th1_under_area_lut",
    "best_th1_over_alpha_median_lut",
    "best_th1_under_alpha_median_lut",
    "best_th1_ou_dot_file",
    "best_th1_ou_dot_path",
]
for k in keys:
    print(k, row[h.index(k)])
