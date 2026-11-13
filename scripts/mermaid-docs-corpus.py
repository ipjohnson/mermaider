"""Writes the examples of mermaid's syntax docs to the MjsFacts corpus.

Usage: python3 -I scripts/mermaid-docs-corpus.py <mermaid checkout> [<out dir>]

Takes every ```mermaid, ```mermaid-example and ```mermaid-nocode fence of packages/mermaid/src/docs/syntax/*.md, except
all-examples.md, and writes it as docs-<doc>-<n>.mmd, where <n> counts the fences of that doc from 1. Examples of a
diagram type Mermaider does not draw are left out; the header list is MjsCorpus.HeaderPattern. The checkout's commit is
written to SOURCE.txt beside them.
"""
import os
import re
import subprocess
import sys

checkout = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), '..', 'tests', 'Mermaider.Tests', 'Snapshots', 'MjsFacts', 'Docs')
src = os.path.join(checkout, 'packages', 'mermaid', 'src', 'docs', 'syntax')

fence = re.compile(r'^```(mermaid(?:-example|-nocode)?)\s*\n(.*?)^```\s*$', re.M | re.S)
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


os.makedirs(out, exist_ok=True)
for stale in os.listdir(out):
    if stale.endswith('.mmd'):
        os.remove(os.path.join(out, stale))

kept = skipped = 0
for name in sorted(os.listdir(src)):
    if not name.endswith('.md') or name == 'all-examples.md':
        continue
    text = open(os.path.join(src, name), encoding='utf-8').read()
    for i, m in enumerate(fence.finditer(text), 1):
        body = m.group(2)
        if not body.strip():
            continue
        if not header.match(first_line(body)):
            skipped += 1
            continue
        with open(os.path.join(out, f'docs-{name[:-3]}-{i:03}.mmd'), 'w', encoding='utf-8', newline='\n') as f:
            f.write(body)
        kept += 1

commit = subprocess.run(['git', '-C', checkout, 'rev-parse', 'HEAD'], capture_output=True, text=True, check=True).stdout.strip()
tags = subprocess.run(['git', '-C', checkout, 'tag', '--points-at', 'HEAD'], capture_output=True, text=True).stdout.split()
tag = next((t for t in tags if t.startswith('mermaid@')), tags[0] if tags else '')
with open(os.path.join(out, 'SOURCE.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('The docs-*.mmd files are the examples of mermaid\'s syntax docs (packages/mermaid/src/docs/syntax, MIT licensed)\n')
    f.write(f'at mermaid-js/mermaid {tag or "(untagged)"} commit {commit}, written by scripts/mermaid-docs-corpus.py.\n')
print(f'{kept} kept, {skipped} of types Mermaider does not draw left out')
