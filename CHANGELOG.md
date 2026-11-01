# Changelog

Each release needs a section here before it's tagged. CI turns these sections into the
release notes and into the update list LiveSplit shows runners.

Format: a `## x.y.z` heading, then one `- ` line per change, written for runners.

## 0.3.0

- LiveSplit now tells you when a new version of Custom Comparisons is out, shows what changed, and installs it for you.

## 0.2.0

- New block: your best run from the last N days, a "recent PB" that moves as you play.
- New block: from runs that reached a segment, for games where most runs reset early.
- New block: a recent-weighted average, where newer times count more.
- New blocks: round to 0.1 s, and keep times between two limits (like never faster than golds, never slower than PB).
- A plain-English user guide with screenshots comes in the download.

## 0.1.0

- First release: build your own comparisons from blocks, with a live preview of your splits.
- 14 blocks: sources, filters, averages and percentiles, math, goal pacing and fallbacks.
- Share comparisons as files or text. They can never contain code.
