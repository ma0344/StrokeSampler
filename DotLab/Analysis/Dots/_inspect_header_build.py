import re
from pathlib import Path

p = Path('DotLab/Analysis/LineN1VsDotN1Matcher.cs')
text = p.read_text(encoding='utf-8')
start = text.index('var header = new StringBuilder')
end = text.index('var headerText = header.ToString()', start)
chunk = text[start:end]

arrays = re.findall(r'AppendHeaderCsvNames\(header, ref hcol, new\[\]\s*\{(.*?)\}\);', chunk, re.S)
print('AppendHeaderCsvNames blocks:', len(arrays))
name_count = 0
for a in arrays:
    qs = re.findall(r'"([^"]+)"', a)
    name_count += len(qs)
print('Total names in AppendHeaderCsvNames:', name_count)

calls = re.findall(r'AppendHeaderShapeWithOverUnder\(header, ref hcol, (\d+)\)', chunk)
print('Shape header calls:', calls)

print('Calculated columns:', 7 + name_count + 32 * len(calls))
