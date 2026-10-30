"""Resolve the snapshot merge: both sides' ids, no duplicates, one line order (goal round 4)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\ValidationTests.cs')
lines = path.read_text(encoding='utf-8').split('\n')

start = next(i for i, line in enumerate(lines) if line.startswith('<<<<<<<'))
mid = next(i for i, line in enumerate(lines) if line.startswith('======='))
end = next(i for i, line in enumerate(lines) if line.startswith('>>>>>>>'))

ours = lines[start + 1:mid]
theirs = lines[mid + 1:end]
merged = ours + [line for line in theirs if line not in ours]
lines[start:end + 1] = merged
path.write_text('\n'.join(lines), encoding='utf-8')
print('resolved with', len(merged), 'lines from both sides')
