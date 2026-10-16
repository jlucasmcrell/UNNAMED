"""Inventory candidate source files: duration, channels, rate, onset (take) count. usage: python -P inv.py <root> <regex> > out.tsv"""
import sys, os, re, soundfile as sf
from alib import load, onsets
root, pat = sys.argv[1], re.compile(sys.argv[2], re.I)
for d, _, fs in os.walk(root):
    for f in fs:
        p = os.path.join(d, f)
        if not f.lower().endswith(('.wav', '.ogg', '.flac')) or not pat.search(p):
            continue
        try:
            i = sf.info(p)
            n = len(onsets(load(p), -28, 0.15)) if i.duration < 400 else -1
        except Exception as e:
            print(f"ERR\t{p}\t{e}"); continue
        print(f"{i.duration:.2f}\t{i.channels}\t{i.samplerate}\t{n}\t{os.path.relpath(p, root)}")
