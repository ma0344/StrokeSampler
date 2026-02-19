import csv

path = r"DotLab/Analysis/Dots/lineN1-vs-dotN1-match-20260211-164727.csv"
with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    header = next(r)
    row = next(r)

missing = header[len(row):]
print("missing count", len(missing))
print("missing columns", missing)
