# Shiori v2.3.9

Shiori v2.3.9 reports the internal indexed-search duration to AI clients.

## Added

- Added `elapsedMilliseconds` to the `search_files` JSON response.
- Defined the measured interval as workspace search, result merging, ranking,
  and summary generation; MCP transport and AI-side waiting are excluded.

## Fixed

- Corrected the interactive MSI dialog sequence.
- Corrected per-user MSI component key paths and empty-directory cleanup so
  WiX validation passes without ICE03, ICE38, or ICE64 errors.
