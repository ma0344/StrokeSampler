import csv
import os

path = os.path.join(os.path.dirname(__file__), "lineN1-vs-dotN1-match-20260211-182745.csv")
with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    h = next(r)
    rows = list(r)

print("header", len(h))
for i, row in enumerate(rows[:5], start=2):
    print("row", i, len(row), "tail", row[-6:])
