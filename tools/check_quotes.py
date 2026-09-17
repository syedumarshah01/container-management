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
                # A string that closes and is immediately followed by { is the shape a swallowed backslash
                # leaves behind: `= $""{path}""` reads as an empty string, then a block, then another empty
                # string, so no state is left open and only this rule can see it.
                if text.startswith('{', i + 1):
                    problems.append((line, 'a string closes and a brace opens right after it - an interpolated '
                                           'hole has fallen outside its quotes, usually because a \\" reached '
                                           'the file as "'))
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


THICK = ('Margin', 'Padding', 'BorderThickness')


def thickness_problems(text):
    """Shapes a Thickness cannot read, for the markup the compiler only checks later.

    An Avalonia margin takes one, two or four numbers. `Margin="0,0,16"` - three - is what a retyped
    `0,0,0,16` looks like when it loses a zero, and the build stops with AVLN2005 naming the line but not the
    cause. Numbers only: a binding is somebody else's business.
    """
    out = []
    for m in re.finditer(r'\b(%s)="([^"]*)"' % '|'.join(THICK), text):
        value = m.group(2)
        if '{' in value:
            continue
        parts = [p.strip() for p in value.split(',')]
        shape = re.compile(r'-?\d*\.?\d+(px)?')
        if len(parts) not in (1, 2, 4) or not all(shape.fullmatch(p) and p for p in parts):
            out.append((text[:m.start()].count('\n') + 1,
                        f'{m.group(1)}="{value}" is not a thickness - one, two or four numbers, never '
                        f'{len(parts)}'))
    return out


THICK_SELF_TEST = [
    ('    <TextBlock Margin="0,0,16" Text="x" />', 'a margin that lost a zero'),
    ('    <TextBlock Padding="4,4,4,4,4" Text="x" />', 'a padding with five numbers'),
    ('    <Border BorderThickness="" />', 'an empty thickness'),
]


SELF_TEST = [
    # the shapes this file exists for, each with the error a compiler would raise on it
    ('            Arguments = $""{Path.GetDirectoryName(path)}"",', 'swallowed backslash'),
    ('                + $"--print-to-pdf="{outPath}" "file";', 'a browser argument whose quotes went missing'),
    ('            Eq("a", "b"\n                "c");\n    }\n}\n', 'adjacent literals without a +'),
]


def self_test():
    """Say so out loud if the gate stops seeing the damage it was written for."""
    bad = 0
    for text, why in SELF_TEST:
        problems, _ = scan(text)
        print(f'    {"caught" if problems else "MISSED"}: {why}')
        bad += 0 if problems else 1
    for text, why in THICK_SELF_TEST:
        problems = thickness_problems(text)
        print(f'    {"caught" if problems else "MISSED"}: {why}')
        bad += 0 if problems else 1
    total = len(SELF_TEST) + len(THICK_SELF_TEST)
    print(); print("self-test: %d/%d shapes caught" % (total - bad, total))
    return 1 if bad else 0


def main():
    if '--self-test' in sys.argv:
        sys.exit(self_test())
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    if args:
        files = args
    else:
        def listing(pattern):
            return subprocess.run(['git', 'ls-files', pattern], capture_output=True, text=True).stdout.split()
        # Pages are markup, not C#, and the compiler only reads them later: a thickness it cannot parse stops a
        # build the same as a bad quote does, so both are walked here.
        files = listing('*.cs') + listing('*.axaml')
    bad = 0
    for f in files:
        try:
            text = open(f, encoding='utf-8-sig').read()
        except OSError as ex:
            print(f'{f}: cannot read ({ex})')
            bad += 1
            continue
        if f.endswith('.axaml'):
            for line, why in thickness_problems(text):
                print(f'{f}:{line}: {why}')
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
