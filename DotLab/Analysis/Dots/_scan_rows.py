import csv

path = r"DotLab/Analysis/Dots/lineN1-vs-dotN1-match-20260211-184337.csv"

with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    header = next(r)
    header_len = len(header)
    print("header_len", header_len)

    min_len = 10**9
    max_len = 0
    min_rows = []
    max_rows = []

    for i, row in enumerate(r, start=2):
        l = len(row)
        if l < min_len:
            min_len = l
            min_rows = [(i, row)]
        elif l == min_len:
            min_rows.append((i, row))

        if l > max_len:
            max_len = l
            max_rows = [(i, row)]
        elif l == max_len:
            max_rows.append((i, row))

    print("min_len", min_len, "rows", [x[0] for x in min_rows[:10]], "count", len(min_rows))
    print("max_len", max_len, "rows", [x[0] for x in max_rows[:10]], "count", len(max_rows))

    # print first min row and first max row tails
    if min_rows:
        i, row = min_rows[0]
        print("min_row", i, "len", len(row), "tail", row[-10:])
    if max_rows:
        i, row = max_rows[0]
        print("max_row", i, "len", len(row), "tail", row[-10:])
