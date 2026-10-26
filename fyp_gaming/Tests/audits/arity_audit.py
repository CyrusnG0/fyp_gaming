"""Arity audit for a C# file: for every method declared in the file, check that each call site
passes between the required and total parameter counts (covers optional params).

Why this exists: the P4 diff check (comparing compiler error-code *sets* on a file whose Unity types
are unresolved) silently misses CS7036/CS1501/CS1503 inside methods whose own signature references
unresolved types -- e.g. `CountSeasonEvents()` survived a 0-warning "clean" check. This script does
not need Unity types: it only compares local declarations with local call sites.

Handles: comments, verbatim/interpolated strings (interpolation code is audited, literals are not),
multi-line parameter lists, optional parameters, generics with commas, comparison operators.
"""
import re
import sys

BS = chr(92)
DQ = chr(34)
SQ = chr(39)
NL = chr(10)


def code_only(src):
    """Return src with comments and string/char *literals* blanked, keeping newlines and keeping the
    code inside interpolation holes (`$"...{expr}..."`)."""
    out = []
    i = 0
    n = len(src)
    # stack of states: 'code' or 'str' (+ whether that string is interpolated) or 'interp'
    state = [('code', False)]
    interp_brace_depth = []

    def emit(ch, keep):
        out.append(ch if keep else ('\n' if ch == '\n' else ' '))

    while i < n:
        kind, flag = state[-1]
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ''

        if kind == 'code':
            if c == '/' and nxt == '/':
                while i < n and src[i] != '\n':
                    emit(src[i], False)
                    i += 1
                continue
            if c == '/' and nxt == '*':
                while i < n and not (src[i] == '*' and i + 1 < n and src[i + 1] == '/'):
                    emit(src[i], False)
                    i += 1
                emit('*', False)
                emit('/', False)
                i += 2
                continue
            if c == DQ:
                # interpolated?  $"...  /  @$"...  /  $@"...
                prev = ''.join(out[-3:])
                interp = prev.rstrip().endswith('$')
                state.append(('str', interp))
                emit(c, False)
                i += 1
                continue
            if c == '#' and nxt == DQ:
                state.append(('str', True))       # raw interpolated string: treat like $"
                emit(c, False)
                i += 1
                continue
            if c == SQ:
                state.append(('char', False))
                emit(c, False)
                i += 1
                continue
            emit(c, True)
            i += 1
            continue

        if kind == 'char':
            if c == BS:
                emit(c, False)
                emit(nxt, False)
                i += 2
                continue
            if c == SQ:
                state.pop()
            emit(c, False)
            i += 1
            continue

        if kind == 'str':
            if flag and c == '{' and nxt == '{':
                emit(c, False)
                emit(nxt, False)
                i += 2
                continue
            if flag and c == '{':
                state.append(('interp', False))
                interp_brace_depth.append(1)
                emit(c, False)
                i += 1
                continue
            if flag and c == '}' and nxt == '}':
                emit(c, False)
                emit(nxt, False)
                i += 2
                continue
            if c == BS:
                emit(c, False)
                emit(nxt, False)
                i += 2
                continue
            if c == DQ:
                state.pop()
            emit(c, False)
            i += 1
            continue

        if kind == 'interp':
            if c == DQ:
                state.append(('str', True))       # nested string inside an interpolation hole
                emit(c, False)
                i += 1
                continue
            if c == '{':
                interp_brace_depth[-1] += 1
                emit(c, True)
                i += 1
                continue
            if c == '}':
                interp_brace_depth[-1] -= 1
                if interp_brace_depth[-1] == 0:
                    interp_brace_depth.pop()
                    state.pop()
                    emit(c, False)                 # blank the interpolation brace itself
                    i += 1
                    continue
                emit(c, True)
                i += 1
                continue
            emit(c, True)
            i += 1
            continue

    return ''.join(out)


def match_paren(src, open_idx):
    depth = 0
    for i in range(open_idx, len(src)):
        if src[i] == '(':
            depth += 1
        elif src[i] == ')':
            depth -= 1
            if depth == 0:
                return i
    return -1


def normalise(span):
    """Blank operators containing angle brackets, then generic argument lists, so the remaining
    < > can never disturb bracket depth."""
    s = re.sub(r'>=|<=|=>|->|>>=|<<=|>>|<<|==|!=|&&|\|\|', lambda m: ' ' * len(m.group(0)), span)
    chars = list(s)
    i = 0
    while i < len(s):
        if s[i] == '<' and i > 0 and (s[i - 1].isalnum() or s[i - 1] in '._]>'):
            depth = 0
            j = i
            while j < len(s):
                if s[j] == '<':
                    depth += 1
                elif s[j] == '>':
                    depth -= 1
                    if depth == 0:
                        break
                j += 1
            if j < len(s) and depth == 0:
                for k in range(i, j + 1):
                    if chars[k] != NL:
                        chars[k] = ' '
                i = j + 1
                continue
        if s[i] in '<>':                       # a lone comparison operator: ignore for depth
            chars[i] = ' '
        i += 1
    return ''.join(chars)


def top_level_commas(span):
    depth = 0
    commas = 0
    for ch in span:
        if ch in '([{':
            depth += 1
        elif ch in ')]}':
            depth -= 1
        elif ch == ',' and depth == 0:
            commas += 1
    return commas


def count_args(span):
    return 0 if span.strip() == '' else top_level_commas(normalise(span)) + 1


def audit(path):
    src = code_only(open(path, encoding='utf-8').read())

    decl_re = re.compile(
        r'^[ \t]*(?:(?:public|private|protected|internal|static|readonly|override|virtual|sealed|async|partial)\s+)*'
        r'[A-Za-z_][\w\.]*\s+([A-Za-z_]\w*)\s*\(',
        re.M)
    keywords = ('if', 'for', 'while', 'foreach', 'switch', 'catch', 'using', 'lock',
                'return', 'new', 'base', 'this')

    decls = {}
    decl_positions = set()
    for m in decl_re.finditer(src):
        name = m.group(1)
        if name in keywords:
            continue
        open_idx = src.index('(', m.start(1))
        close_idx = match_paren(src, open_idx)
        if close_idx < 0:
            continue
        params = src[open_idx + 1:close_idx]
        total = count_args(params)
        required = total - len(re.findall(r'=', normalise(params)))
        decls.setdefault(name, []).append((required, total, src[:m.start(1)].count(NL) + 1))
        decl_positions.add(open_idx)

    problems = []
    for name in sorted(decls):
        req, tot, _ = decls[name][0]
        for m in re.finditer(r'\b' + re.escape(name) + r'\s*\(', src):
            open_idx = src.index('(', m.start())
            if open_idx in decl_positions:
                continue
            close_idx = match_paren(src, open_idx)
            if close_idx < 0:
                continue
            given = count_args(src[open_idx + 1:close_idx])
            if not (req <= given <= tot):
                line = src[:m.start()].count(NL) + 1
                problems.append("%s:%d: %s called with %d args, declared %d..%d"
                                % (path, line, name, given, req, tot))
    return decls, problems


def main(argv):
    if not argv:
        sys.exit("usage: python arity_audit.py <file.cs> [<file.cs> ...]")
    exit_code = 0
    for p in argv:
        decls, problems = audit(p)
        print("--- %s: %d local methods audited ---" % (p, len(decls)))
        for line in problems:
            print("  ARITY MISMATCH " + line)
        if not problems:
            print("  no arity mismatches")
        else:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
