# ADR-004: xxHash64 for copy verification

- Status: Accepted
- Date: 2026-11-23

## Context

The old script verified by file size only. That misses a corrupted copy of the same size.

## Decision

Use **xxHash64** from `System.IO.Hashing`.

- The source hash is computed while copying, so each source byte is read once.
- The destination is re-read after it is flushed to disk and hashed again; the two must match.
- xxHash64 is non-cryptographic. That is fine here: we detect accidental corruption, not tampering.
  SHA-256 can be added later as an option for archives.

## Consequences

- Verification costs one extra read of each destination file.
- Checksums are written to `copy-report.txt` inside the project.
