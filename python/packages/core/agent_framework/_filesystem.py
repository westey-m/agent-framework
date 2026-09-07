# Copyright (c) Microsoft. All rights reserved.

"""Private filesystem security helpers."""

from __future__ import annotations

import hashlib
import stat
from base64 import urlsafe_b64encode
from pathlib import Path

WINDOWS_RESERVED_FILE_STEMS: frozenset[str] = frozenset({
    "CON",
    "PRN",
    "AUX",
    "NUL",
    "COM1",
    "COM2",
    "COM3",
    "COM4",
    "COM5",
    "COM6",
    "COM7",
    "COM8",
    "COM9",
    "LPT1",
    "LPT2",
    "LPT3",
    "LPT4",
    "LPT5",
    "LPT6",
    "LPT7",
    "LPT8",
    "LPT9",
    "COM¹",
    "COM²",
    "COM³",
    "LPT¹",
    "LPT²",
    "LPT³",
})

MAX_ENCODED_STORAGE_KEY_SEGMENT_LENGTH = 180
_DIGEST_SEGMENT_MARKER = "sha256."


def is_link_or_reparse_point(path: Path) -> bool:
    """Return whether ``path`` is a symbolic link, junction, or other reparse point."""
    path_stat = path.lstat()
    if stat.S_ISLNK(path_stat.st_mode):
        return True

    is_junction = getattr(path, "is_junction", None)
    if callable(is_junction) and is_junction():
        return True

    reparse_attribute = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    file_attributes = getattr(path_stat, "st_file_attributes", 0)
    return bool(reparse_attribute and file_attributes & reparse_attribute)


def is_literal_storage_key_segment_safe(value: str) -> bool:
    """Return whether an opaque identifier can be used verbatim as one path segment.

    The predicate is deliberately conservative because the result is used as a
    security boundary (see :func:`storage_key_segment`). Only ASCII alphanumerics
    and ``.``/``_``/``-`` are accepted, so a literal segment can never be folded
    onto a different identifier by filesystem Unicode normalization (macOS APFS
    and HFS+ fold NFD and NFC forms of the same text onto one directory entry).

    Values that are not literal-safe are encoded rather than rejected.
    """
    if (
        not value
        or value.startswith(".")
        or value.endswith((" ", "."))
        or value.split(".", maxsplit=1)[0].upper() in WINDOWS_RESERVED_FILE_STEMS
    ):
        return False
    if any(ord(character) < 32 for character in value):
        return False
    return all(character.isascii() and (character.isalnum() or character in "._-") for character in value)


def storage_key_segment(value: str, *, encoded_prefix: str) -> str:
    """Return a filesystem-safe path segment for an opaque identifier.

    This is the single derivation used everywhere an identifier that participates
    in an isolation boundary (a session ID, an owner ID, a memory scope) is turned
    into a storage location. Callers must not pre-normalize the value: a general
    path canonicalizer is lossy, and identifiers that differ only in separators or
    surrounding whitespace would then share one storage namespace.

    The mapping is **injective**: two byte-distinct values never produce the same
    segment. Literal-safe values are returned verbatim; everything else is encoded
    under ``encoded_prefix``. The two namespaces cannot overlap because
    :func:`is_literal_storage_key_segment_safe` rejects every value starting with
    ``~``, which each ``encoded_prefix`` begins with.

    Args:
        value: The opaque identifier to convert.

    Keyword Args:
        encoded_prefix: Component-specific prefix applied to encoded values, so
            that two components encoding the same identifier stay distinguishable.
            Must start with ``~`` to stay outside the literal namespace.

    Returns:
        A single path segment containing no path separators.
    """
    if is_literal_storage_key_segment_safe(value):
        return value
    encoded_value = urlsafe_b64encode(value.encode("utf-8")).decode("ascii").rstrip("=")
    encoded_segment = f"{encoded_prefix}{encoded_value}"
    if len(encoded_segment) <= MAX_ENCODED_STORAGE_KEY_SEGMENT_LENGTH:
        return encoded_segment
    # ``.`` is outside the base64url alphabet, so a digest segment can never
    # collide with the base64 encoding of some other (shorter) identifier.
    digest = hashlib.sha256(value.encode("utf-8")).hexdigest()
    return f"{encoded_prefix}{_DIGEST_SEGMENT_MARKER}{digest}"
