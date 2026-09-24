# ADR 0006: Installer does not own shiori.ini

## Status

Accepted.

## Context

Up to v2.4.0 the MSI wrote `config\shiori.ini` through the Windows Installer
IniFile table in the `LanguageConfig` component (`Permanent`, `NeverOverwrite`).
During a major upgrade the old product was removed first, and its uninstall ran
`RemoveIniValues`, deleting the `language` entry and, with no other entries, the
file. The new product then skipped the component because `NeverOverwrite` saw
the old registry key path during costing. The v2.3.10 to v2.4.0 upgrade lost
the user's `language=ja-JP` this way.

## Decision

- `shiori.ini` is user data, not an MSI resource. The installer runs the
  deferred, impersonated custom action `InitializeConfig`, which calls
  `shiori config init --language [SHIORI_LANGUAGE]` after `InstallFiles`. The
  command creates the file only when it does not exist.
- `LanguageConfig` keeps its GUID and registry key path but no IniFile row, so
  it stays shared with older products and their uninstall leaves the file.
- `MajorUpgrade` uses `Schedule="afterInstallExecute"`, so the old product is
  removed after the new one is installed and shared components keep a client.

## Alternatives

- Keep the IniFile table with other attributes: rejected; the old product's
  uninstall still removes its INI entries during the upgrade.
- Remember the language in the registry and rewrite the file: rejected; it
  overwrites later manual edits and still loses other sections.

## Consequences

- Installing a new version never changes an existing `shiori.ini`; the language
  selected in the setup UI applies only to a first installation.
- `afterInstallExecute` requires strict component rules. Generated file
  components already use path-derived stable GUIDs.
- Uninstall leaves `shiori.ini` in place, as with other user data.

## Verification

- Unit tests cover `config init` creation, preservation of an existing file,
  and rejection of unsupported languages.
- Upgrading the real installation from v2.4.0 to v2.4.1 kept `shiori.ini` and
  `mcp-token.bin` byte-identical; the log showed `LanguageConfig` action `Null`
  for the removed product and one registered related product afterwards.
