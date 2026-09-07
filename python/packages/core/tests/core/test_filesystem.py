# Copyright (c) Microsoft. All rights reserved.

"""Tests for the shared storage-key derivation in ``agent_framework._filesystem``.

Every component that maps a caller-controlled identifier (a session ID, an owner
ID, a memory scope) onto a storage location routes through
:func:`storage_key_segment`. Its defining property is **injectivity**: two
byte-distinct identifiers must never produce the same segment, because the
segment is an isolation boundary.
"""

from __future__ import annotations

import pytest

from agent_framework._filesystem import (  # pyright: ignore[reportPrivateUsage]
    MAX_ENCODED_STORAGE_KEY_SEGMENT_LENGTH,
    is_literal_storage_key_segment_safe,
    storage_key_segment,
)

# Identifiers that a lossy path normalizer would fold together, plus values that
# are unsafe as a literal filename. Shared with the per-provider parity tests so
# every component is held to the same contract.
COLLIDING_IDENTIFIERS: tuple[str, ...] = (
    "customer-42",
    "customer-42/",
    "customer-42//",
    "customer-42\\",
    "customer-42\\\\",
    "/customer-42",
    "//customer-42",
    " customer-42",
    "customer-42 ",
    " customer-42 ",
    "\tcustomer-42",
    "customer-42/.",
    "./customer-42",
    "customer-42/..",
)

UNSAFE_IDENTIFIERS: tuple[str, ...] = (
    "",
    ".",
    "..",
    ".hidden",
    "trailing.",
    "trailing ",
    "CON",
    "con",
    "CON.txt",
    "NUL.log",
    "LPT1",
    "COM\u00b9",
    "with\nnewline",
    "with\x00null",
    "caf\u00e9",  # NFC
    "cafe\u0301",  # NFD - byte-distinct from the NFC form above
    "\u65e5\u672c\u8a9e",
    "a/b",
    "a\\b",
    "a:b",
    "a*b",
    "a b",
)

SAFE_IDENTIFIERS: tuple[str, ...] = (
    "customer-42",
    "customer_42",
    "customer.42",
    "0",
    "a",
    "0198f0c5-1a2b-7c3d-8e4f-5a6b7c8d9e0f",
    "session-1",
    "CONSOLE",
    "CONt",
    "x" * 200,
)


def test_safe_identifiers_are_used_verbatim() -> None:
    """An already-safe identifier keeps its literal folder name.

    This is what keeps existing deployments from needing a data migration.
    """
    for value in SAFE_IDENTIFIERS:
        assert is_literal_storage_key_segment_safe(value)
        assert storage_key_segment(value, encoded_prefix="~scope-") == value


def test_unsafe_identifiers_are_encoded_not_rejected() -> None:
    for value in UNSAFE_IDENTIFIERS:
        assert not is_literal_storage_key_segment_safe(value)
        segment = storage_key_segment(value, encoded_prefix="~scope-")
        assert segment.startswith("~scope-")


def test_derivation_is_injective_over_colliding_identifiers() -> None:
    """The MSRC case: values a path normalizer folds together stay distinct."""
    segments = {value: storage_key_segment(value, encoded_prefix="~scope-") for value in COLLIDING_IDENTIFIERS}
    assert len(set(segments.values())) == len(COLLIDING_IDENTIFIERS), segments


def test_derivation_is_injective_over_every_known_identifier() -> None:
    all_values = (*COLLIDING_IDENTIFIERS, *UNSAFE_IDENTIFIERS, *SAFE_IDENTIFIERS)
    unique_values = set(all_values)
    segments = {storage_key_segment(value, encoded_prefix="~scope-") for value in unique_values}
    assert len(segments) == len(unique_values)


def test_unicode_normalization_forms_do_not_collide() -> None:
    """NFC and NFD spellings are byte-distinct and must stay distinct.

    A literal non-ASCII folder name would be folded onto one directory entry by
    macOS APFS/HFS+, so non-ASCII values are always encoded.
    """
    nfc = storage_key_segment("caf\u00e9", encoded_prefix="~scope-")
    nfd = storage_key_segment("cafe\u0301", encoded_prefix="~scope-")
    assert nfc != nfd


def test_segments_never_contain_path_separators() -> None:
    for value in (*COLLIDING_IDENTIFIERS, *UNSAFE_IDENTIFIERS, *SAFE_IDENTIFIERS):
        segment = storage_key_segment(value, encoded_prefix="~scope-")
        assert "/" not in segment
        assert "\\" not in segment


def test_literal_namespace_cannot_collide_with_encoded_namespace() -> None:
    """No literal-safe value can look like an encoded segment.

    ``~`` is outside the literal charset, so an attacker cannot pick a plain
    identifier that lands on some other identifier's encoded folder.
    """
    assert not is_literal_storage_key_segment_safe("~scope-Y3VzdG9tZXItNDIv")
    literal = storage_key_segment("customer-42", encoded_prefix="~scope-")
    encoded = storage_key_segment("customer-42/", encoded_prefix="~scope-")
    assert not literal.startswith("~")
    assert encoded.startswith("~")


def test_prefix_separates_components_for_the_same_identifier() -> None:
    """Two components encoding the same value stay in distinct folders."""
    value = "customer-42/"
    assert storage_key_segment(value, encoded_prefix="~scope-") != storage_key_segment(value, encoded_prefix="~todo-")


def test_long_identifiers_fall_back_to_a_digest_segment() -> None:
    long_value = "customer-42/" + "x" * 400
    segment = storage_key_segment(long_value, encoded_prefix="~scope-")
    assert segment.startswith("~scope-sha256.")
    assert len(segment) <= MAX_ENCODED_STORAGE_KEY_SEGMENT_LENGTH


def test_digest_segments_stay_injective() -> None:
    first = storage_key_segment("a" * 400, encoded_prefix="~scope-")
    second = storage_key_segment("a" * 401, encoded_prefix="~scope-")
    assert first != second


def test_digest_marker_is_outside_the_base64_alphabet() -> None:
    """A base64url encoding can never be mistaken for a digest segment.

    ``.`` is not a base64url character, so the two encoded forms occupy
    disjoint namespaces even though they share a prefix.
    """
    encoded = storage_key_segment("customer-42/", encoded_prefix="~scope-")
    assert "." not in encoded.removeprefix("~scope-")


@pytest.mark.parametrize("value", ["customer-42", "customer-42/", "caf\u00e9", "a" * 400])
def test_derivation_is_deterministic(value: str) -> None:
    assert storage_key_segment(value, encoded_prefix="~scope-") == storage_key_segment(value, encoded_prefix="~scope-")
