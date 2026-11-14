"""Writes the diagrams of mermaid's own rendering and parser tests to the MjsFacts corpus.

Usage: python3 -I scripts/mermaid-test-corpus.py <mermaid checkout> [<out dir>]

Rendering tests: every e2e/diagrams/**/*.mmd fixture, written as e2e-<path>.mmd with the path's slashes as dashes.
Parser tests: every string literal of packages/mermaid/src/diagrams/**/*.spec.{js,ts}, or chain of literals joined with
+, written as parser-<spec>-<n>.mmd, where <n> counts the kept sources of the spec files with that name from 1. A parser source is
dedented and trimmed; a template literal with ${...} is left out, because its text is only known when the test runs.

Only sources whose first line (after front matter and %% comments) is a header Mermaider draws are kept; the header list
is MjsCorpus.HeaderPattern. A source that is already in the corpus (Docs/, Cases/, or an earlier test source) is left
out; sources are compared dedented, with trailing spaces and blank edge lines removed. The checkout's tag and commit are
written to SOURCE.txt beside them.
"""
import collections
import os
import re
import subprocess
import sys

checkout = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
facts = os.path.join(here, '..', 'tests', 'Mermaider.Tests', 'Snapshots', 'MjsFacts')
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(facts, 'Suite')

header = re.compile(
    r'^(flowchart|graph|stateDiagram(-v2)?|classDiagram(-v2)?|sequenceDiagram|erDiagram|pie|quadrantChart|timeline'
    r'|gitGraph|radar-beta|treemap-beta|venn-beta|mindmap|gantt|journey|C4(Context|Container|Component|Dynamic|Deployment)'
    r'|sankey(-beta)?|xychart(-beta)?|requirementDiagram|packet(-beta)?|kanban|architecture-beta|block(-beta)?'
    r'|treeView(-beta)?)\b')


def first_line(body):
    in_front_matter = False
    for raw in body.split('\n'):
        line = raw.strip()
        if line == '---':
            in_front_matter = not in_front_matter
            continue
        if in_front_matter or not line or line.startswith('%%'):
            continue
        return line
    return ''


def normalize(body):
    lines = [l.rstrip() for l in body.replace('\r\n', '\n').split('\n')]
    while lines and not lines[0]:
        lines.pop(0)
    while lines and not lines[-1]:
        lines.pop()
    indents = [len(l) - len(l.lstrip(' \t')) for l in lines if l]
    cut = min(indents) if indents else 0
    return '\n'.join(l[cut:] for l in lines) + '\n'


ESCAPES = {'n': '\n', 't': '\t', 'r': '\r', '\\': '\\', "'": "'", '"': '"', '`': '`', '0': '\0', 'b': '\b', 'f': '\f',
           'v': '\v', '$': '$', '\n': ''}
REGEX_BEFORE = set('(,=:[!&|?{};+-*%<>~^') | {''}
# The title of a describe(), it() or test() call, such as describe('mindmap color selection', ...), is not a diagram.
TEST_TITLE = re.compile(r'\b(?:describe|it|test|bench)(?:\.\w+)*\(\s*\Z')


def literals(code):
    """Yields each string literal chain of JS/TS code: its decoded text, or None when it has an interpolation."""
    i, n = 0, len(code)
    chain, joined = None, False
    prev = ''

    def flush():
        nonlocal chain
        if chain is not None:
            yield_list.append(chain)
        chain = None

    yield_list = []
    while i < n:
        c = code[i]
        if code.startswith('//', i):
            i = code.find('\n', i)
            i = n if i < 0 else i
            continue
        if code.startswith('/*', i):
            i = code.find('*/', i + 2)
            i = n if i < 0 else i + 2
            continue
        if c.isspace():
            i += 1
            continue
        if c in '\'"`':
            title = TEST_TITLE.search(code, 0, i) is not None and not joined
            text, dynamic, i = read_string(code, i)
            if title:
                flush()
                joined = False
                prev = 'str'
                continue
            if chain is not None and joined:
                chain = None if chain[0] is None or dynamic else (chain[0] + text,)
                chain = chain if chain is not None else (None,)
            else:
                flush()
                chain = (None,) if dynamic else (text,)
            joined = False
            prev = 'str'
            continue
        if c == '+' and chain is not None and not code.startswith('++', i):
            joined = True
            i += 1
            continue
        if c == '/' and prev in REGEX_BEFORE:
            i = skip_regex(code, i)
            flush()
            prev = 'regex'
            continue
        flush()
        joined = False
        prev = c if not (c.isalnum() or c in '_$') else 'word'
        i += 1
    flush()
    return [c[0] for c in yield_list]


def read_string(code, i):
    quote = code[i]
    i += 1
    out = []
    dynamic = False
    while i < len(code):
        c = code[i]
        if c == '\\' and i + 1 < len(code):
            e = code[i + 1]
            if e == 'u' and code[i + 2:i + 3] == '{':
                end = code.index('}', i)
                out.append(chr(int(code[i + 3:end], 16)))
                i = end + 1
            elif e == 'u':
                out.append(chr(int(code[i + 2:i + 6], 16)))
                i += 6
            elif e == 'x':
                out.append(chr(int(code[i + 2:i + 4], 16)))
                i += 4
            else:
                out.append(ESCAPES.get(e, e))
                i += 2
            continue
        if c == quote:
            return ''.join(out), dynamic, i + 1
        if quote == '`' and code.startswith('${', i):
            dynamic = True
            i = skip_braces(code, i + 1)
            continue
        if c == '\n' and quote != '`':
            return ''.join(out), True, i
        out.append(c)
        i += 1
    return ''.join(out), True, i


def skip_braces(code, i):
    depth = 0
    while i < len(code):
        c = code[i]
        if c in '\'"`':
            _, _, i = read_string(code, i)
            continue
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return i


def skip_regex(code, i):
    i += 1
    in_class = False
    while i < len(code) and code[i] != '\n':
        c = code[i]
        if c == '\\':
            i += 2
            continue
        if c == '[':
            in_class = True
        elif c == ']':
            in_class = False
        elif c == '/' and not in_class:
            i += 1
            while i < len(code) and code[i].isalpha():
                i += 1
            return i
        i += 1
    return i


seen = set()
for corpus in ('Docs', 'Cases'):
    folder = os.path.join(facts, corpus)
    for name in os.listdir(folder):
        if name.endswith('.mmd'):
            seen.add(normalize(open(os.path.join(folder, name), encoding='utf-8').read()))

os.makedirs(out, exist_ok=True)
for stale in os.listdir(out):
    if stale.endswith('.mmd'):
        os.remove(os.path.join(out, stale))

counts = {'e2e': 0, 'parser': 0, 'duplicate': 0, 'other type': 0}
written = set()
numbers = collections.Counter()


def keep(kind, name, body, write):
    if not header.match(first_line(body)):
        counts['other type'] += 1
        return False
    key = normalize(body)
    if key in seen:
        counts['duplicate'] += 1
        return False
    seen.add(key)
    written.add(name)
    with open(os.path.join(out, name + '.mmd'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(write)
    counts[kind] += 1
    return True


e2e = os.path.join(checkout, 'e2e', 'diagrams')
for root, dirs, files in os.walk(e2e):
    dirs.sort()
    for file in sorted(files):
        if not file.endswith('.mmd'):
            continue
        rel = os.path.relpath(os.path.join(root, file), e2e)[:-4]
        name = 'e2e-' + re.sub(r'[^A-Za-z0-9]+', '-', rel).strip('-').lower()
        # Two paths can make the same name, such as a-b.mmd and a_b.mmd.
        suffix = 1
        while (f'{name}-{suffix}' if suffix > 1 else name) in written:
            suffix += 1
        body = open(os.path.join(root, file), encoding='utf-8').read().replace('\r\n', '\n')
        keep('e2e', f'{name}-{suffix}' if suffix > 1 else name, body, body)

diagrams = os.path.join(checkout, 'packages', 'mermaid', 'src', 'diagrams')
for root, dirs, files in os.walk(diagrams):
    dirs.sort()
    for file in sorted(files):
        if not re.search(r'\.spec\.[jt]s$', file):
            continue
        spec = re.sub(r'\.spec\.[jt]s$', '', file)
        spec = re.sub(r'[^A-Za-z0-9]+', '-', spec).strip('-').lower()
        code = open(os.path.join(root, file), encoding='utf-8').read()
        # Spec files of different diagrams can share a name (parser.spec.ts), so the count goes on across them.
        for text in literals(code):
            if text is None or not text.strip():
                continue
            body = normalize(text)
            if keep('parser', f'parser-{spec}-{numbers[spec] + 1:03}', body, body):
                numbers[spec] += 1

commit = subprocess.run(['git', '-C', checkout, 'rev-parse', 'HEAD'], capture_output=True, text=True, check=True).stdout.strip()
tags = subprocess.run(['git', '-C', checkout, 'tag', '--points-at', 'HEAD'], capture_output=True, text=True).stdout.split()
tag = next((t for t in tags if t.startswith('mermaid@')), tags[0] if tags else '')
with open(os.path.join(out, 'SOURCE.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('The e2e-*.mmd files are the fixtures of mermaid\'s rendering tests (e2e/diagrams), and the parser-*.mmd files\n')
    f.write('the diagram sources of its parser tests (packages/mermaid/src/diagrams/**/*.spec.*), MIT licensed, at\n')
    f.write(f'mermaid-js/mermaid {tag or "(untagged)"} commit {commit}, written by scripts/mermaid-test-corpus.py.\n')
print(f"{counts['e2e']} rendering and {counts['parser']} parser sources kept; {counts['duplicate']} already in the corpus "
      f"and {counts['other type']} of types Mermaider does not draw left out")
