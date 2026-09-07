# UI / UX

## Goal
A calm, native-feeling Windows backup utility, not an “AI-looking” dashboard. Hebrew/RTL is first-class; localization must not be hard-coded into business logic.

## Primary flows
Onboarding/unlock → choose folders → explain recovery responsibility → start first backup → passive status/tray → browse/search backups → select restore → destination/conflict choice → progress → verified result.

## UX rules
Use plain language. Always distinguish `Protected`, `Backing up`, `Paused`, `Offline`, `Needs attention`, and `Restore verified`. Never show “safe” before durable remote completion. Errors explain what happened, whether data is at risk, and next action. Destructive actions require explicit confirmation.

## RTL
Mirrored layout where appropriate, correct mixed Hebrew/Latin/path rendering, logical keyboard navigation, screen-reader labels, focus states, scalable text. File paths/code-like strings may require LTR isolation inside RTL UI.

## Performance perception
Never freeze UI for scan/hash/network work. Show meaningful progress without fake percentages. Large lists virtualize. Search is debounced/cancellable.
