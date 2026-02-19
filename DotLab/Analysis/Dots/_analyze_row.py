import csv
import os

path = os.path.join(os.path.dirname(__file__), "lineN1-vs-dotN1-match-20260211-173104.csv")

with open(path, newline="", encoding="utf-8") as f:
    r = csv.reader(f)
    header = next(r)
    header_len = len(header)

    rows = list(r)

print('header_len', header_len)
row3 = rows[1]  # file row 3 (1-based in file, excluding header): line_pressure=0.2
print('row3_len', len(row3))
extra = row3[header_len:]
print('extra_len', len(extra), 'extra', extra)

# show around the boundary where extras start
start = header_len - 10
end = min(len(row3), header_len + 10)
print('--- boundary ---')
for idx in range(start, end):
    name = header[idx] if idx < header_len else '(extra)'
    print(idx+1, name, row3[idx])
