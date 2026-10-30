"""Fix the chain checker's ref pattern: a flow-style `creature_ref: x,` captured a trailing comma, which read as "no definition"."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\verify_fauna_chain.py')
code = path.read_text(encoding='utf-8')
old = "re.findall(r'creature_ref: (\\S+)', text)"
new = "re.findall(r'creature_ref: ([A-Za-z0-9_.]+)', text)"
assert old in code, 'anchor missing'
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('refs read without trailing punctuation')
