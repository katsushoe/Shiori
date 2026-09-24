# Shiori v2.4.1

Shiori v2.4.1 keeps user settings across MSI upgrades.

## Added

- Added `shiori config init [--language <en-US|ja-JP>]`, which creates
  `config\shiori.ini` only when it does not exist.

## Fixed

- MSI upgrades no longer delete `config\shiori.ini`. Earlier installers managed
  the file through the Windows Installer IniFile table, so removing the old
  version during an upgrade deleted the `language` entry. The installer now
  calls `shiori config init` and removes the previous version after installing
  the new one.

## Upgrade notes

- If an earlier upgrade removed `config\shiori.ini`, recreate it with
  `shiori config init --language <en-US|ja-JP>` and add any
  `[index] exclude_patterns` value again.
