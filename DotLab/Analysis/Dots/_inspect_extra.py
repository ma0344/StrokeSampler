import csv

path = r"DotLab/Analysis/Dots/lineN1-vs-dotN1-match-20260211-164727.csv"

with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    header = next(r)
    header_len = len(header)

    for i, row in enumerate(r, start=2):
        if len(row) > header_len:
            extra = row[header_len:]
            print("row", i, "len", len(row), "extra_len", len(extra))
            print("extra_values", extra)
            break
