import csv

path = r"DotLab/Analysis/Dots/lineN1-vs-dotN1-match-20260211-164727.csv"
with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    header = next(r)
    row = next(r)

print("header", len(header))
print("row", len(row))
print("last headers", header[-10:])
print("last row", row[-10:])
