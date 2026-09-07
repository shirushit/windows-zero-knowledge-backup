# Restore Engine

Restore is a release gate.

## Modes
Single file, folder subtree, selected items, full supported dataset. Restore to original or alternate root.

## Safety
Remote data is hostile until authenticated. Reject path traversal, absolute-path injection, invalid names, device paths, and escape from restore root. Define conflict behavior (overwrite/rename/skip) explicitly in UI. Write to temporary files, verify, then atomically replace where possible.

## Fidelity
Preserve directory hierarchy and supported creation/modified timestamps. Permissions/ACLs and special filesystem features must be explicitly declared supported or unsupported.

## Disaster recovery drill
A clean machine/profile with only installer + credentials/recovery material must be able to discover/retrieve the remote catalog and restore a deterministic fixture dataset with hashes matching the source.
