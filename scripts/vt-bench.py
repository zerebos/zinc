"""How fast native/<arch>/ghostty-vt.dll parses terminal output, for comparing Ghostty commits.

Feeds seeded data through ghostty_terminal_vt_write (the call Zinc makes for every read of a program's output), 64 KB
at a time, into a 120x80 terminal with Zinc's scrollback (10,000 lines, no byte limit). A warm-up run, then the best of 7 per workload.
The data is made here, in the style of Ghostty's own ghostty-gen (plain text, mixed UTF-8, styled text), and cached
in .scratch/bench/data.

Before bumping the Ghostty commit:   python scripts/vt-bench.py --save before
After rebuilding the DLL:           python scripts/vt-bench.py --compare before
A drop of more than ~10% is worth a look before shipping. Plain Python, no packages.
"""

import argparse
import ctypes as C
import json
import pathlib
import random
import time

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / ".scratch" / "bench" / "data"
RESULTS = ROOT / ".scratch" / "bench" / "results"
SIZE = 20 * 1024 * 1024
VERSION = 1  # bump when the generators change, so old cached data isn't compared with new


def ascii_text(rng):
    out, size = [], 0
    while size < SIZE:
        line = "".join(rng.choice("abcdefghijklmnopqrstuvwxyz0123456789 .,;:-_()[]{}") for _ in range(rng.randint(10, 118)))
        out.append(line + "\r\n")
        size += len(line) + 2
    return "".join(out).encode()


def utf8_text(rng):
    pools = ["abcdefghijklmnopqrstuvwxyz ", "àéîõüßçñ ", "日本語漢字のテキスト", "가나다라마바사", "αβγδεζηθ", "🙂🚀✨🎉"]
    out, size = [], 0
    while size < SIZE:
        pool = rng.choice(pools)
        line = "".join(rng.choice(pool) for _ in range(rng.randint(5, 50)))
        out.append(line + "\r\n")
        size += len(line.encode()) + 2
    return "".join(out).encode()


def styled_text(rng):
    out, size = [], 0
    while size < SIZE:
        parts = []
        for _ in range(rng.randint(2, 8)):
            kind = rng.random()
            if kind < 0.4:
                sgr = str(rng.choice([1, 3, 4, 7, 31, 32, 33, 34, 35, 36, 91, 92, 93, 94]))
            elif kind < 0.7:
                sgr = f"38;5;{rng.randint(0, 255)}"
            else:
                sgr = f"{rng.choice([38, 48])};2;{rng.randint(0, 255)};{rng.randint(0, 255)};{rng.randint(0, 255)}"
            text = "".join(rng.choice("abcdefghijklmnopqrstuvwxyz0123456789 ") for _ in range(rng.randint(4, 16)))
            parts.append(f"\x1b[{sgr}m{text}\x1b[0m")
        line = "".join(parts) + "\r\n"
        out.append(line)
        size += len(line)
    return "".join(out).encode()


WORKLOADS = {"ascii": ascii_text, "utf8": utf8_text, "styled": styled_text}


def data_for(name):
    path = DATA / f"vt-bench-{name}-v{VERSION}.bin"
    if not path.exists():
        DATA.mkdir(parents=True, exist_ok=True)
        path.write_bytes(WORKLOADS[name](random.Random(7))[:SIZE])
    return path.read_bytes()


def measure(dll, data):
    chunks = [data[i:i + 65536] for i in range(0, len(data), 65536)]
    best = None
    for run in range(8):  # the first is a warm-up (cold caches and CPU clocks made it ~10% slower)
        t = C.c_void_p()
        dll.ghostty_terminal_new(None, C.byref(t), 120, 80)
        lines = C.c_size_t(10000)
        dll.ghostty_terminal_set(t, 28, C.byref(lines))  # SCROLLBACK_MAX_LINES
        dll.ghostty_terminal_set(t, 27, None)            # SCROLLBACK_MAX_BYTES: none
        start = time.perf_counter()
        for chunk in chunks:
            dll.ghostty_terminal_vt_write(t, chunk, len(chunk))
        took = time.perf_counter() - start
        dll.ghostty_terminal_free(t)
        if run > 0:
            best = took if best is None else min(best, took)
    return len(data) / best / (1024 * 1024)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dll", default=str(ROOT / "native" / "x64" / "ghostty-vt.dll"))
    parser.add_argument("--save", metavar="NAME", help="keep the results as NAME")
    parser.add_argument("--compare", metavar="NAME", help="compare with results kept as NAME")
    args = parser.parse_args()

    dll = C.CDLL(args.dll)
    dll.ghostty_terminal_vt_write.argtypes = [C.c_void_p, C.c_char_p, C.c_size_t]
    dll.ghostty_terminal_set.argtypes = [C.c_void_p, C.c_int, C.c_void_p]
    base = json.loads((RESULTS / f"{args.compare}.json").read_text()) if args.compare else {}

    results = {}
    for name in WORKLOADS:
        mbs = results[name] = measure(dll, data_for(name))
        line = f"{name:7} {mbs:8.0f} MB/s"
        if name in base:
            change = (mbs - base[name]) / base[name] * 100
            line += f"   {args.compare}: {base[name]:.0f} MB/s ({change:+.0f}%){'   <-- slower' if change < -10 else ''}"
        print(line)

    if args.save:
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"{args.save}.json").write_text(json.dumps(results, indent=2))
        print(f"saved as {args.save}")


if __name__ == "__main__":
    main()
