"""Structural diff of two Underrail save data files (.dat), using the crafter's NRBF reader.

Usage: python3 savediff.py <a.dat> <b.dat> [--limit N]
Walks both object graphs from the root and prints paths whose class or value differ.
Object identity (BinaryFormatter ids) is ignored; shared references are walked once per graph.
"""
import sys
import os
import zlib

# nrbf.py (MS-NRBF reader) comes from the underrail-item-crafter project; set NRBF_PATH to its folder if it lives elsewhere.
sys.path.insert(0, os.environ.get('NRBF_PATH', os.path.expanduser('~/projects/underrail-item-crafter/tools')))
import nrbf  # noqa: E402


def load(path):
    raw = open(path, 'rb').read()
    data = zlib.decompressobj(31).decompress(raw[24:]) if raw[24:26] == b'\x1f\x8b' else raw[24:]
    r = nrbf.Reader(data)
    root = r.parse()
    for o in list(r.objs.values()):
        nrbf.resolve(o, r.objs)
    return root


def flatten(v, path, out, seen):
    if isinstance(v, (dict, list)):
        if id(v) in seen:
            out[path] = '<ref ' + seen[id(v)] + '>'
            return
        seen[id(v)] = path
    if isinstance(v, nrbf.Obj):
        out[path] = '<' + str(v.cls) + '>'
        for k in v:
            flatten(v[k], path + '/' + str(k), out, seen)
    elif isinstance(v, dict):
        for k in v:
            flatten(v[k], path + '/' + str(k), out, seen)
    elif isinstance(v, list):
        out[path] = '<list %d>' % len(v)
        for i, x in enumerate(v):
            flatten(x, '%s[%d]' % (path, i), out, seen)
    else:
        out[path] = repr(v)


def main():
    a, b = sys.argv[1], sys.argv[2]
    limit = int(sys.argv[sys.argv.index('--limit') + 1]) if '--limit' in sys.argv else 60
    fa, fb = {}, {}
    flatten(load(a), '', fa, {})
    flatten(load(b), '', fb, {})
    only_a = [k for k in fa if k not in fb]
    only_b = [k for k in fb if k not in fa]
    changed = [k for k in fa if k in fb and fa[k] != fb[k] and not fa[k].startswith('<ref')]
    print(f'paths: {len(fa)} vs {len(fb)}; only in A: {len(only_a)}; only in B: {len(only_b)}; changed: {len(changed)}')
    for title, keys, fmt in (('ONLY IN A', only_a, lambda k: fa[k]), ('ONLY IN B', only_b, lambda k: fb[k]),
                             ('CHANGED', changed, lambda k: f'{fa[k]} -> {fb[k]}')):
        for k in keys[:limit]:
            print(f'{title}: {k} = {fmt(k)[:160]}')


if __name__ == '__main__':
    main()
