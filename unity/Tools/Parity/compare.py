"""Compares two smoke-test outputs (unity/NATIVE_PLAN.md section 4): screenshots, log facts, errors and scene dumps, or
perf runs.

  python compare.py <ref dir> <branch dir> [--thresholds t.json] [--report report.html]
  python compare.py <ref dir> <ref2 dir> --calibrate thresholds.json      # noise floor: writes per-shot thresholds
  python compare.py --perf <dir made by run_perf.ps1>

Exit code 0 when nothing FAILed (REVIEW items are listed for a person to look at), 1 otherwise.
Needs Pillow and numpy.
"""
import argparse
import difflib
import html
import json
import math
import os
import re
import statistics
import sys

import numpy as np
from PIL import Image

FLOOR = {'mean': 0.75, 'changed': 0.3, 'ssim': 0.99}   # absolute floors: mean diff (0-255), % pixels off by >16, SSIM
FAIL_SSIM = 0.9
REVIEW_ONLY = ['0-loading.png']                         # taken part way through loading: always REVIEW, never FAIL

# ---------------------------------------------------------------------------------------------------------- images


def load(path):
    return np.asarray(Image.open(path).convert('RGB'), dtype=np.float32)


def box(a, r):
    """Mean over a (2r+1)^2 window, with an integral image."""
    p = np.pad(a, r + 1, mode='edge')
    c = p.cumsum(0).cumsum(1)
    n = 2 * r + 1
    s = c[n:, n:] - c[:-n, n:] - c[n:, :-n] + c[:-n, :-n]
    return s[:a.shape[0], :a.shape[1]] / (n * n)


def ssim(a, b):
    la = a @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    lb = b @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    r, c1, c2 = 3, (0.01 * 255) ** 2, (0.03 * 255) ** 2
    ma, mb = box(la, r), box(lb, r)
    va, vb = box(la * la, r) - ma * ma, box(lb * lb, r) - mb * mb
    cov = box(la * lb, r) - ma * mb
    m = ((2 * ma * mb + c1) * (2 * cov + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2))
    return float(m.mean())


def image_metrics(ref, br):
    a, b = load(ref), load(br)
    if a.shape != b.shape:
        return {'size': f'{a.shape[1]}x{a.shape[0]} vs {b.shape[1]}x{b.shape[0]}'}, None
    d = np.abs(a - b)
    dmax = d.max(axis=2)
    return {
        'mean': float(d.mean()),
        'changed': float((dmax > 16).mean() * 100),
        'p99': float(np.percentile(dmax, 99)),
        'ssim': ssim(a, b),
    }, dmax


def heatmap(dmax, path):
    v = np.clip(dmax * 4, 0, 255).astype(np.uint8)
    rgb = np.stack([v, (v * 0.35).astype(np.uint8), 255 - v], axis=2)
    rgb[dmax == 0] = (16, 16, 24)
    Image.fromarray(rgb).save(path)


def find(root, ext):
    out = []
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if x not in ('saves', 'pictures', '_report')]
        for f in files:
            if f.endswith(ext):
                out.append(os.path.relpath(os.path.join(d, f), root).replace('\\', '/'))
    return sorted(out)


def limits_for(rel, thresholds):
    t = dict(FLOOR)
    t.update(thresholds.get('default', {}))
    t.update(thresholds.get('shots', {}).get(rel, {}))
    return t


def judge(rel, m, t):
    if 'size' in m:
        return 'FAIL', 'size ' + m['size']
    why = []
    if m['mean'] > t['mean']:
        why.append(f"mean {m['mean']:.2f} > {t['mean']:.2f}")
    if m['changed'] > t['changed']:
        why.append(f"changed {m['changed']:.2f}% > {t['changed']:.2f}%")
    if m['ssim'] < t['ssim']:
        why.append(f"ssim {m['ssim']:.4f} < {t['ssim']:.4f}")
    if any(rel.endswith(r) for r in REVIEW_ONLY):
        return 'REVIEW', 'review-only shot' + (': ' + '; '.join(why) if why else '')
    if m['ssim'] < FAIL_SSIM:
        return 'FAIL', '; '.join(why)
    return ('REVIEW', '; '.join(why)) if why else ('PASS', '')

# ------------------------------------------------------------------------------------------------------------ logs


TIMING = re.compile(r'fps average|slowest parts|pacing|loaded after|reloaded with the save after|^\[AutoShot\] \d+\.\d+s |'
                    r'slow loading frame|saved |writing to|context:|loading screen:|harness:|\] done$|scene dump|Autosave\] main thread| ms\b')
NUM = re.compile(r'-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?')


def round_num(m):
    x = float(m.group(0))
    if x == 0 or not math.isfinite(x):
        return '0'
    return f'{x:.3g}'


def facts(log):
    out = []
    for line in log:
        if '[AutoShot]' not in line or TIMING.search(line):
            continue
        line = line[line.index('[AutoShot]'):].strip()
        line = re.sub(r'[A-Za-z]:[\\/][^\s,;]*', '<path>', line)
        out.append(NUM.sub(round_num, line))
    return out


ERR = re.compile(r'Exception|\berror\b|NullReference|IndexOutOfRange', re.I)
ERR_IGNORE = re.compile(r'Fallback handler could not load library|d3d12|Shader warning|0 error', re.I)


def errors(log):
    out = set()
    for line in log:
        if ERR.search(line) and not ERR_IGNORE.search(line):
            out.add(NUM.sub('#', re.sub(r'[A-Za-z]:[\\/][^\s,;]*', '<path>', line.strip()))[:300])
    return out


def read_log(path):
    if not os.path.exists(path):
        return None
    with open(path, encoding='utf-8', errors='replace') as f:
        return f.read().splitlines()

# ----------------------------------------------------------------------------------------------------- scene dumps


def num_diff(a, b, tol):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) != len(b) or any(num_diff(x, y, tol) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) > tol * max(1.0, abs(a), abs(b)) if isinstance(a, float) or isinstance(b, float) else a != b
    return a != b


def compare_dump(ref, br, tol=2e-3):
    a, b = json.load(open(ref, encoding='utf-8')), json.load(open(br, encoding='utf-8'))
    issues = []
    # pair renderers by what they are first: the same world-space vertices (vhash) and vertex count, whatever their path
    # (the native world moves objects between parents and changes sibling order); what is left is paired by path
    ra_list, rb_list = a['renderers'], b['renderers']
    by_content = {}
    for i, r in enumerate(rb_list):
        if r.get('vhash'):
            by_content.setdefault((r['vhash'], r['verts']), []).append(i)
    pairs, used_b, left_a = [], set(), []
    for r in ra_list:
        cands = by_content.get((r.get('vhash'), r.get('verts'))) if r.get('vhash') else None
        if cands:
            j = cands.pop(0)
            used_b.add(j)
            pairs.append((r['path'], r, rb_list[j]))
        else:
            left_a.append(r)
    rest_b = {r['path']: r for i, r in enumerate(rb_list) if i not in used_b}
    missing = []
    for r in left_a:
        if r['path'] in rest_b:
            pairs.append((r['path'], r, rest_b.pop(r['path'])))
        else:
            missing.append(r['path'])
    # renderers of the other scenes (the cleaning bench, the item photos) are drawn only by those scenes' own cameras; the
    # bridge made them the first time those were drawn, the native ones exist from the start: an extra one there shows nowhere
    extra = sorted(p for p in rest_b if p.split('/')[0] == 'Main')
    # and the bridge kept a find's bench objects after the bench closed, the native bench lets them go: not drawn either
    missing = [p for p in missing if p.split('/')[0] == 'Main']
    ra = {r['path']: r for r in ra_list}
    rb = {r['path']: r for r in rb_list}
    for p in missing[:20]:
        issues.append(f'missing renderer {p}')
    for p in extra[:20]:
        issues.append(f'extra renderer {p}')
    if len(missing) > 20 or len(extra) > 20:
        issues.append(f'... {len(missing)} missing, {len(extra)} extra in all')
    changed = 0
    for p, x, y in pairs:
        # the bridge never re-reads the inside of a static group, so the load-time warm-up left those renderers with 100 km
        # "never culled" bounds (NATIVE_LOG.md, S2-S5); native objects have their real bounds: same pixels, bounds not compared
        # (scaled parents shrink the 100 km box: no real object reaches 5 km, the largest is the beach's 1.3 km surround)
        unculled = max(abs(v) for v in x.get('min', [0]) + x.get('max', [0])) > 5000
        keys = [k for k in x if k != 'path' and not (unculled and k in ('min', 'max')) and (k not in y or num_diff(x[k], y[k], tol))]
        if keys:
            changed += 1
            if changed <= 30:
                issues.append(f'{p}: ' + ', '.join(f'{k} {json.dumps(x.get(k))} -> {json.dumps(y.get(k))}'[:200] for k in keys[:4]))
    if changed > 30:
        issues.append(f'... {changed} renderers differ in all')
    # lights by kind and place, not path: the native world moves the sun and spot lights out of the world root (plan 2.1)
    def lkey(l):
        return l['type'] + '@' + ','.join(f'{v:.1f}' for v in l['pos'])
    la = {lkey(l): dict(l, path=None) for l in a['lights']}
    lb = {lkey(l): dict(l, path=None) for l in b['lights']}
    for k in sorted(set(la) ^ set(lb)):
        issues.append(('missing' if k in la else 'extra') + f' light {k}')
    for k in sorted(set(la) & set(lb)):
        keys = [f for f in la[k] if num_diff(la[k][f], lb[k].get(f), tol)]
        if keys:
            issues.append(f'light {k}: ' + ', '.join(keys))
    for g, v in a['globals'].items():
        if num_diff(v, b['globals'].get(g), tol):
            issues.append(f'global {g}: {v} -> {b["globals"].get(g)}')
    return issues, len(ra), len(rb)

# ------------------------------------------------------------------------------------------------------------ main


def environment(log):
    """The display and VSync a run had (UI_PLAN.md section 5): the context line's screen, dpi and OS ratio, and the
    pacing lines' vsync. Runs that differ here differ in pictures and UI timing for reasons that are not the code's."""
    if log is None:
        return {}
    if isinstance(log, list):
        log = chr(10).join(log)
    env = {}
    m = re.search(r'\[AutoShot\] context: .*?screen (\d+x\d+ at \d+ Hz, \w+, dpi \d+).*?OS ratio ([\d.]+)', log)
    if m:
        env['display'] = m.group(1)
        env['os ratio'] = m.group(2)
    v = re.search(r'pacing: .*?vsync (\d+)', log)
    if v:
        env['vsync'] = v.group(1)
    return env


def check_environment(ref, br, ran):
    """Stops the comparison when the branch ran on another display or with other settings than the reference."""
    for d in sorted(ran):
        pa, pb = os.path.join(ref, d, 'Player.log'), os.path.join(br, d, 'Player.log')
        if not (os.path.exists(pa) and os.path.exists(pb)):
            continue
        ea, eb = environment(read_log(pa)), environment(read_log(pb))
        diff = {k: (ea.get(k), eb.get(k)) for k in set(ea) | set(eb) if ea.get(k) != eb.get(k)}
        if diff:
            print(f'ENVIRONMENT DIFFERS in run "{d}" (reference vs branch):')
            for k, (x, y) in sorted(diff.items()):
                print(f'  {k}: {x}  vs  {y}')
            print('Pictures and UI timing would differ for that reason alone. Retake the reference on this display and with')
            print('the fixed test settings (run_suites.ps1 sets them), or pass --any-environment to compare anyway.')
            return False
        return True
    return True


def compare(ref, br, thresholds, report, calibrate, any_environment=False):
    rows, worst = [], 'PASS'
    # only the runs (top-level folders) the branch actually made are compared
    ran = {d for d in os.listdir(br) if os.path.isdir(os.path.join(br, d)) and d != '_report'}
    if not any_environment and not check_environment(ref, br, ran):
        return 3

    def in_run(rel):
        return '/' not in rel or rel.split('/')[0] in ran
    shots = [r for r in find(ref, '.png') if in_run(r)]
    branch_shots = set(find(br, '.png'))
    rep_dir = os.path.join(br, '_report')
    os.makedirs(rep_dir, exist_ok=True)
    cal = {'default': dict(FLOOR), 'shots': {}}
    for rel in shots:
        if rel not in branch_shots:
            rows.append((rel, 'FAIL', 'missing in branch', None, None))
            continue
        m, dmax = image_metrics(os.path.join(ref, rel), os.path.join(br, rel))
        if calibrate:
            if 'size' not in m:
                cal['shots'][rel] = {'mean': max(FLOOR['mean'], round(2 * m['mean'], 3)),
                                     'changed': max(FLOOR['changed'], round(2 * m['changed'], 3)),
                                     'ssim': min(FLOOR['ssim'], round(1 - 2 * (1 - m['ssim']), 5))}
            status, why = ('PASS', '') if m.get('ssim', 0) >= FLOOR['ssim'] and m.get('mean', 99) <= FLOOR['mean'] and m.get('changed', 99) <= FLOOR['changed'] else ('NOISY', 'over the absolute floor')
            if any(rel.endswith(r) for r in REVIEW_ONLY):
                status = 'REVIEW'
        else:
            status, why = judge(rel, m, limits_for(rel, thresholds))
        hm = None
        if dmax is not None and status != 'PASS':
            hm = os.path.join(rep_dir, rel.replace('/', '__'))
            heatmap(dmax, hm)
        rows.append((rel, status, why, m, hm))
    for rel in sorted(branch_shots - set(shots)):
        rows.append((rel, 'REVIEW', 'extra in branch', None, None))

    # logs: errors and facts, run by run
    log_rows = []
    for rel in [r for r in find(ref, 'Player.log') if in_run(r)]:
        la, lb = read_log(os.path.join(ref, rel)), read_log(os.path.join(br, rel))
        if lb is None:
            log_rows.append((rel, 'FAIL', ['no log in branch']))
            continue
        new_err = sorted(errors(lb) - errors(la))
        fa, fb = facts(la), facts(lb)
        diff = [d for d in difflib.unified_diff(fa, fb, lineterm='', n=0) if not d.startswith(('---', '+++', '@@'))]
        status = 'FAIL' if new_err or diff else 'PASS'
        log_rows.append((rel, status, [f'new error: {e}' for e in new_err[:10]] + diff[:40]))

    dump_rows = []
    for rel in [r for r in find(ref, '.scene.json') if in_run(r)]:
        pb = os.path.join(br, rel)
        if not os.path.exists(pb):
            dump_rows.append((rel, 'REVIEW', ['no scene dump in branch'], 0, 0))
            continue
        issues, na, nb = compare_dump(os.path.join(ref, rel), pb)
        dump_rows.append((rel, 'REVIEW' if issues else 'PASS', issues, na, nb))

    if calibrate:
        with open(calibrate, 'w', encoding='utf-8') as f:
            json.dump(cal, f, indent=1, sort_keys=True)
        print(f'wrote {calibrate} ({len(cal["shots"])} shots)')

    def count(rs, s):
        return sum(1 for r in rs if r[1] == s)
    for name, rs in (('shots', rows), ('logs', log_rows), ('scene dumps', dump_rows)):
        print(f'{name}: ' + ', '.join(f'{count(rs, s)} {s}' for s in ('PASS', 'NOISY', 'REVIEW', 'FAIL') if count(rs, s)))
    for r in rows:
        if r[1] != 'PASS':
            m = r[3]
            ms = '' if not m or 'size' in m else f" (mean {m['mean']:.2f}, changed {m['changed']:.2f}%, ssim {m['ssim']:.4f})"
            print(f'  {r[1]:6} {r[0]}: {r[2]}{ms}')
    for r in log_rows:
        if r[1] != 'PASS':
            print(f'  {r[1]:6} {r[0]}')
            for line in r[2][:12]:
                print('         ' + line)
    for r in dump_rows:
        if r[1] != 'PASS':
            print(f'  {r[1]:6} {r[0]} ({r[3]} -> {r[4]} renderers)')
            for line in r[2][:8]:
                print('         ' + line)

    if report:
        write_report(report, ref, br, rows, log_rows, dump_rows)
        print(f'report: {report}')
    failed = any(r[1] == 'FAIL' for r in rows + log_rows) and not calibrate
    return 1 if failed else 0


def uri(p):
    return 'file:///' + os.path.abspath(p).replace('\\', '/')


def write_report(path, ref, br, rows, log_rows, dump_rows):
    h = ['<!doctype html><meta charset="utf-8"><title>Parity report</title><style>',
         'body{font:14px system-ui;margin:16px;background:#111;color:#ddd} table{border-collapse:collapse} td,th{padding:4px 8px;border-bottom:1px solid #333;vertical-align:top}',
         '.PASS{color:#7c7}.REVIEW,.NOISY{color:#ec5}.FAIL{color:#f66} img{width:420px;cursor:pointer} pre{white-space:pre-wrap;margin:0}</style>',
         f'<h1>Parity report</h1><p>reference <code>{html.escape(ref)}</code><br>branch <code>{html.escape(br)}</code></p>',
         '<p>Click a branch picture to flicker it against the reference.</p><h2>Shots</h2><table>']
    order = {'FAIL': 0, 'REVIEW': 1, 'NOISY': 2, 'PASS': 3}
    for r in sorted(rows, key=lambda r: (order[r[1]], r[0])):
        rel, status, why, m = r[0], r[1], r[2], r[3]
        hm = r[4] if len(r) > 4 else None
        ms = '' if not m or 'size' in m else f"mean {m['mean']:.2f}<br>changed {m['changed']:.2f}%<br>p99 {m['p99']:.0f}<br>ssim {m['ssim']:.4f}"
        imgs = ''
        if status != 'PASS':
            a, b = uri(os.path.join(ref, rel)), uri(os.path.join(br, rel))
            imgs = (f'<img src="{a}" title="reference"> <img src="{b}" title="branch" data-a="{a}" data-b="{b}" '
                    f'onclick="this.src=this.src==this.dataset.a?this.dataset.b:this.dataset.a">' + (f' <img src="{uri(hm)}" title="difference">' if hm else ''))
        h.append(f'<tr><td class="{status}">{status}</td><td>{html.escape(rel)}<br><small>{html.escape(why)}</small></td><td>{ms}</td><td>{imgs}</td></tr>')
    h.append('</table><h2>Logs</h2><table>')
    for rel, status, lines in log_rows:
        h.append(f'<tr><td class="{status}">{status}</td><td>{html.escape(rel)}</td><td><pre>{html.escape(chr(10).join(lines))}</pre></td></tr>')
    h.append('</table><h2>Scene dumps</h2><table>')
    for rel, status, lines, na, nb in dump_rows:
        h.append(f'<tr><td class="{status}">{status}</td><td>{html.escape(rel)}<br><small>{na} -> {nb} renderers</small></td><td><pre>{html.escape(chr(10).join(lines))}</pre></td></tr>')
    h.append('</table>')
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(h))

# ------------------------------------------------------------------------------------------------------------ perf


MEASURE = re.compile(r'\[AutoShot\] (?P<label>.+?): (?P<fps>[\d.]+) fps average, slowest frame (?P<worst>\d+) ms; '
                     r'Unity main thread (?P<main>-?[\d.]+) ms, render thread (?P<render>[\d.]+) ms, GPU (?P<gpu>[\d.]+) ms; '
                     r'game(?:\+sync)? (?P<game>[\d.]+) ms')
PACING = re.compile(r'\[AutoShot\] (?P<label>.+?) pacing: frame ms median (?P<median>[\d.]+), 90% [\d.]+, 99% (?P<p99>[\d.]+); (?P<late>\d+) of')
READY = re.compile(r'ready after (?P<ready>[\d.]+) s')

# (metric, higher is worse, allowed change in %, and an absolute slack under which a change is noise). The slack comes
# from running the reference against itself (NATIVE_LOG.md, S0): 0.01 ms on a 0.13 ms scene is +7.7%, and late frames
# go 0 -> 1 or 7 -> 4 between identical runs.
PERF_LIMITS = [('fps', False, 5, 0), ('game', True, 5, 0.05), ('p99', True, 10, 0.5), ('late', True, None, 3),
               ('ready', True, 10, 0.3), ('main', True, None, 0), ('render', True, None, 0), ('gpu', True, None, 0),
               ('median', True, None, 0)]


def perf_runs(log):
    scenes = {}
    for line in log:
        m = MEASURE.search(line)
        if m:
            scenes.setdefault(m['label'], {}).update({k: float(m[k]) for k in ('fps', 'main', 'render', 'gpu', 'game')})
        m = PACING.search(line)
        if m:
            scenes.setdefault(m['label'], {}).update({'median': float(m['median']), 'p99': float(m['p99']), 'late': float(m['late'])})
        m = READY.search(line)
        if m:
            scenes.setdefault('(loading)', {})['ready'] = float(m['ready'])
    return scenes


def perf(root):
    data = {'ref': [], 'branch': []}
    for d in sorted(os.listdir(root)):
        which = d.split('_')[0]
        log = read_log(os.path.join(root, d, 'Player.log'))
        if which in data and log:
            data[which].append(perf_runs(log))
    if not data['ref'] or not data['branch']:
        print('need ref_N and branch_N folders with Player.log in', root)
        return 1

    def med(runs, scene, key):
        vals = [r[scene][key] for r in runs if scene in r and key in r[scene]]
        return statistics.median(vals) if vals else None
    scenes = sorted({s for r in data['ref'] for s in r})
    bad = 0
    print(f'{len(data["ref"])} reference and {len(data["branch"])} branch runs; medians, branch vs reference')
    for s in scenes:
        parts = []
        for key, worse_up, limit, slack in PERF_LIMITS:
            a, b = med(data['ref'], s, key), med(data['branch'], s, key)
            if a is None or b is None:
                continue
            ch = (b - a) / abs(a) * 100 if a else (0 if b == a else 100)
            flag = ''
            worse = (b - a) if worse_up else (a - b)
            if key == 'late':
                regress = worse > slack
            elif limit is not None:
                regress = ((ch > limit) if worse_up else (-ch > limit)) and worse > slack
            else:
                regress = False
            if regress:
                flag, bad = ' !', bad + 1
            parts.append(f'{key} {a:g} -> {b:g} ({ch:+.1f}%){flag}')
        print(f'  {s}: ' + '; '.join(parts))
    print('perf: ' + ('no regressions over the limits' if bad == 0 else f'{bad} regression(s) over the limits (marked !)'))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ref', nargs='?')
    ap.add_argument('branch', nargs='?')
    ap.add_argument('--thresholds')
    ap.add_argument('--report')
    ap.add_argument('--calibrate')
    ap.add_argument('--perf')
    ap.add_argument('--any-environment', action='store_true', help='compare even if the display or VSync differ')
    a = ap.parse_args()
    if a.perf:
        sys.exit(perf(a.perf))
    if not a.ref or not a.branch:
        ap.error('give a reference and a branch folder, or --perf <folder>')
    thresholds = json.load(open(a.thresholds, encoding='utf-8')) if a.thresholds else {}
    sys.exit(compare(a.ref, a.branch, thresholds, a.report or os.path.join(a.branch, '_report', 'report.html'), a.calibrate, a.any_environment))


if __name__ == '__main__':
    main()
