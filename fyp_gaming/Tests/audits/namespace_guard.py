"""Namespace guard for a C# file: checks that every attribute and every fully-qualified type usage
resolves through a namespace the file actually imports.

Why this exists: a standalone compile of a Unity script resolves UnityEngine as CS0234 wholesale, so
it cannot tell you that `[UnityEngine.FormerlySerializedAs]` is wrong (the attribute lives in
`UnityEngine.Serialization`). This guard knows where each Unity attribute lives and whether the file
imports that namespace -- the same class of error the Unity smoke test caught twice.

Usage: python namespace_guard.py <file.cs> [<file.cs> ...]
"""
import re
import sys

# attribute -> the namespace that declares it (Unity 6 / .NET as used in this project)
ATTRIBUTE_NAMESPACE = {
    "SerializeField": "UnityEngine",
    "Header": "UnityEngine",
    "Tooltip": "UnityEngine",
    "Range": "UnityEngine",
    "DefaultExecutionOrder": "UnityEngine",
    "RequireComponent": "UnityEngine",
    "DisallowMultipleComponent": "UnityEngine",
    "ExecuteInEditMode": "UnityEngine",
    "ExecuteAlways": "UnityEngine",
    "HideInInspector": "UnityEngine",
    "AddComponentMenu": "UnityEngine",
    "ContextMenu": "UnityEngine",
    "CreateAssetMenu": "UnityEngine",
    "FormerlySerializedAs": "UnityEngine.Serialization",
    "Serializable": "System",
    "NonSerialized": "System",
}

USING_RE = re.compile(r'^\s*using\s+(?:static\s+)?([A-Za-z_][\w\.]*)\s*;', re.M)
# [Namespace.Type( ... )]  or  [Type( ... )]
ATTR_RE = re.compile(r'\[\s*([A-Za-z_][\w\.]*)\s*(?:\(|\])')
# any fully-qualified type-ish usage: Namespace[.Namespace]*.Type, at least two dots of structure
QUALIFIED_RE = re.compile(r'\b([A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)+)\b')


def audit(path):
    src = open(path, encoding="utf-8").read()
    usings = set(USING_RE.findall(src))

    problems = []

    # 1) attributes: is the declaring namespace imported?
    for m in ATTR_RE.finditer(src):
        used = m.group(1)
        name = used.split('.')[-1]
        declared_in = ATTRIBUTE_NAMESPACE.get(name)
        if declared_in is None:
            continue
        line = src[:m.start()].count("\n") + 1
        if '.' in used:
            given = used.rsplit('.', 1)[0]
            if given != declared_in:
                problems.append("%s:%d: [%s] is declared in %s, not %s"
                                % (path, line, used, declared_in, given))
            elif given not in usings:
                problems.append("%s:%d: [%s] needs `using %s;`" % (path, line, used, declared_in))
        elif declared_in not in usings:
            problems.append("%s:%d: [%s] is declared in %s -- needs `using %s;`"
                            % (path, line, name, declared_in, declared_in))

    # 2) every fully-qualified usage elsewhere in the file
    for m in QUALIFIED_RE.finditer(src):
        full = m.group(1)
        line = src[:m.start()].count("\n") + 1
        declared_in = ATTRIBUTE_NAMESPACE.get(full.split('.')[-1])
        if declared_in is not None:
            given = full.rsplit('.', 1)[0]
            if given != declared_in:
                problems.append("%s:%d: %s is declared in %s, not %s"
                                % (path, line, full, declared_in, given))

    return usings, problems


def main(argv):
    if not argv:
        sys.exit("usage: python namespace_guard.py <file.cs> [<file.cs> ...]")
    exit_code = 0
    for p in argv:
        usings, problems = audit(p)
        print("--- %s: using %s ---" % (p, ", ".join(sorted(usings))))
        for line in problems:
            print("  NAMESPACE PROBLEM " + line)
        if not problems:
            print("  no namespace problems")
        else:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
