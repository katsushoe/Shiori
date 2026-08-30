# Shiori v2.3.10

Shiori v2.3.10 packages the validated per-user Windows installer corrections.

## Changed

- Updated the product version to 2.3.10.
- Included the corrected WiX dialog sequence, component key paths, and
  directory cleanup metadata in the Windows installer.
- Removed the unsupported macOS CI and packaging workflow. Windows remains the
  only officially supported distribution platform.

## Fixed

- Preserved the existing language setting during MSI upgrades.
