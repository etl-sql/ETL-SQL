#!/usr/bin/env python3
"""Audits docs/syntax-index.md against the reference documentation tree.

The index bills itself as a central map of every keyword, command, function and option,
so two things must hold:

  1. Every ``reference/**`` link in the index resolves to a file that exists.
  2. Every reference page is reachable from the index.

Run from the repository root::

    python scripts/Audit-SyntaxIndex.py           # report
    python scripts/Audit-SyntaxIndex.py --strict  # exit 1 if anything is wrong

Deliberately NOT audited here: statement coverage derived from AST type names. The
CamelCase type name is not the surface syntax -- TryCatchStatement is written
``BEGIN TRY``, CreatePgpKeyPairStatement is ``CREATE PGP_KEY_PAIR`` -- so matching on
type names produces false gaps. Auditing that dimension needs syntax derived from the
parser's token dispatch, not from type names.
"""
import argparse
import os
import re
import sys

REFERENCE_LINK = re.compile(r'\]\((reference/[^)#]+\.md)\)')


def audit(repo_root):
    index_path = os.path.join(repo_root, 'docs', 'syntax-index.md')
    with open(index_path, encoding='utf-8') as handle:
        index = handle.read()

    linked = set()
    broken = []
    for link in REFERENCE_LINK.findall(index):
        target = os.path.normpath(os.path.join(repo_root, 'docs', link))
        linked.add(target)
        if not os.path.exists(target):
            broken.append(link)

    pages = []
    for root, _dirs, files in os.walk(os.path.join(repo_root, 'docs', 'reference')):
        for name in files:
            if name.endswith('.md') and name.lower() != 'readme.md':
                pages.append(os.path.normpath(os.path.join(root, name)))

    unlinked = sorted(p for p in pages if p not in linked)
    return pages, broken, unlinked


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--strict', action='store_true',
                        help='exit non-zero when the index has broken or missing links')
    args = parser.parse_args()

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    pages, broken, unlinked = audit(repo_root)

    print('Syntax index audit')
    print('  reference pages (excluding README): %d' % len(pages))
    print('  broken links in index:              %d' % len(broken))
    print('  pages not linked from index:        %d' % len(unlinked))

    if broken:
        print('\nBroken links:')
        for link in broken:
            print('  ', link)

    if unlinked:
        print('\nNot linked from syntax-index.md:')
        for page in unlinked:
            print('  ', os.path.relpath(page, repo_root).replace(os.sep, '/'))

    if args.strict and (broken or unlinked):
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
