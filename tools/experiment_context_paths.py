"""Path and hashing helpers.

Display paths are portable report text (repo-relative where possible); absolute paths exist only so
that input hashes and file reads never depend on the process working directory.
"""
import hashlib
import os


def display_path(path, repo_root=None):
    """Portable, deterministic path text: relative to the repo when possible, forward slashes."""
    if path is None:
        return None
    text = str(path)
    if repo_root is not None:
        try:
            text = os.path.relpath(os.path.abspath(text), os.path.abspath(str(repo_root)))
        except ValueError:
            text = str(path)
    return text.replace("\\", "/")


def absolute_path(path):
    """The real path used for reading and hashing, independent of the working directory."""
    if path is None:
        return None
    return os.path.abspath(str(path))


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
