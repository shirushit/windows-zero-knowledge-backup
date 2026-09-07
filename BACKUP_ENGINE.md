# Backup Engine

## Requirements
Initial + incremental backup; configurable roots/exclusions; avoid re-uploading unchanged content; handle renames/deletes according to retention policy; cope with locked/changing files; persistent job state; bandwidth/CPU throttling.

## Correctness rules
Never trust timestamps alone for all correctness decisions. Define a stable-file-read policy: collect metadata, read/hash, re-check metadata, and retry/defer if the file changed during capture. Symlinks/reparse points, sparse files, permissions, very long paths, hidden/system files, and filesystem errors require explicit policies.

## Versioning
Catalog each backup version/snapshot consistently. Deletion locally must not automatically destroy historical remote data unless retention policy explicitly says so.

## Deduplication
MVP may start conservatively. Any cross-file/content dedupe scheme must be reviewed for metadata leakage and complexity before enabling.
