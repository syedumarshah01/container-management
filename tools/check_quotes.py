#!/usr/bin/env python3
"""The gate a build should not have had to be.

    python3 tools/check_quotes.py            # whole repo
    python3 tools/check_quotes.py path.cs     # one file

ProBooks is edited by a program that writes C# through a scripting language, and twice that cost a build: a
backslash swallowed on the way (`$"text "{path}"" ` where `\"` was meant), a raw string the counter did not
know, an interpolated string with a `+ "more"` on the next line, which is C and not C#. Each one reads as a
syntax error, and while the file has a syntax error the compiler stays quiet about everything it cannot bind -
so one bad quote can hide five other faults behind it.

This walks the text the way the compiler walks it - code, string, verbatim, raw, char, comment - and says where
a state never closes and how braces and parentheses drift. It is not a compiler and does not try to be: it is
the thirty seconds that stops a build failing on somebody else's evening.
"""
import re
import subprocess
import sys

OPEN_RAW = '"""'


def scan(text):
    """Return (problems, brace/paren/bracket drift)."""
    i, n, line = 0, len(text), 1
    state, start = 'code', 1
    problems = []
    depth = {'{}': 0, '()': 0, '[]': 0}
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ''
        third = text[i + 2] if i + 2 < n else ''
        if c == '\n':
            line += 1
        if state == 'code':
            if text.startswith(OPEN_RAW, i):
                j = text.find(OPEN_RAW, i + 3)
                if j < 0:
                    problems.append((line, 'raw string """ opened and never closed'))
                    break
                line += text.count('\n', i, j)
                i = j + 3
                continue
            if c == '/' and nxt == '/':
                state, start = 'line', line
                i += 2
                continue
            if c == '/' and nxt == '*':
                state, start = 'block', line
                i += 2
                continue
            if c == '@' and nxt == '"':
                state, start = 'verbatim', line
                i += 2
                continue
            if c == '$' and nxt == '@' and third == '"':
                state, start = 'verbatim', line
                i += 3
                continue
            if c == '$' and nxt == '"':
                state, start = 'interp', line
                i += 2
                continue
            if c == '"':
                state, start = 'string', line
                i += 1
                continue
            if c == "'":
                state, start = 'char', line
                i += 1
                continue
            for key in depth:
                if c == key[0]:
                    depth[key] += 1
                elif c == key[1]:
                    depth[key] -= 1
            i += 1
            continue
        if state == 'line':
            i += 1
            if c == '\n':
                state = 'code'
            continue
        if state == 'block':
            if c == '*' and nxt == '/':
                state = 'code'
                i += 2
                continue
            i += 1
            continue
        if state == 'verbatim':
            if c == '"':
                if nxt == '"':
                    i += 2
                    continue
                state = 'code'
            i += 1
            continue
        if state in ('string', 'interp'):
            if c == '\\':
                i += 2
                continue
            if c == '"':
                state = 'code'
                i += 1
                continue
            if c == '\n':
                problems.append((start, 'a string opens here and does not close on this line - a quote meant to '
                                        'be escaped as \\" is probably unescaped'))
                state = 'code'
                continue
            i += 1
            continue
        if state == 'char':
            if c == '\\':
                i += 2
                if i < n and text[i - 1] == '\n':
                    problems.append((start, 'a char literal does not close before the line ends'))
                    state = 'code'
                continue
            if c == "'":
                state = 'code'
                i += 1
                continue
            if c == '\n':
                problems.append((start, 'a char literal does not close before the line ends'))
                state = 'code'
                continue
            i += 1
            continue
    if state in ('string', 'interp', 'verbatim', 'char', 'block'):
        problems.append((start, f'file ends still inside {state}'))
    # Adjacent string literals: legal in C, not in C#.
    for m in re.finditer(r'"(?:[^"\\\n]|\\.)*"[ \t]*\n[ \t]*"', text):
        problems.append((text[:m.start()].count('\n') + 1,
                         'a string ends a line and another begins the next with no + between them'))
    return problems, depth


def main():
    args = sys.argv[1:]
    if args:
        files = args
    else:
        files = subprocess.run(['git', 'ls-files', '*.cs'], capture_output=True, text=True).stdout.split()
    bad = 0
    for f in files:
        try:
            text = open(f, encoding='utf-8-sig').read()
        except OSError as ex:
            print(f'{f}: cannot read ({ex})')
            bad += 1
            continue
        problems, depth = scan(text)
        drift = {k: v for k, v in depth.items() if v}
        if problems or drift:
            bad += 1
            print(f'{f}')
            for line, why in problems[:8]:
                print(f'    line {line}: {why}')
            if drift:
                print(f'    unbalanced: {drift}')
    verb = 'file has' if bad == 1 else 'files have'
    print(f'\n{len(files)} checked, {bad} {verb} a problem' if bad else f'\n{len(files)} checked, nothing to report')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
