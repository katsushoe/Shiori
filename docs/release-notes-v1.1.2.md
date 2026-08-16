# Shiori v1.1.2

Shiori v1.1.2 standardizes the Windows runtime layout.

## Changes

- Installs executable files under `bin` and adds that directory to user PATH.
- Creates `config`, `logs`, and `data` under the selected installation root.
- Preserves configuration, logs, and application data during uninstall.
- Uses the installation `data` directory by default for workspace registrations
  and indexes; `SHIORI_DATA_HOME` remains available as an override.
- Extends `shiori doctor` to verify all writable standard directories.
- Bundles ripgrep 15.2.0 so text search works without a separate installation.
