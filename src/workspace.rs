use std::fmt;
use std::path::{Path, PathBuf};

/// Canonical workspace boundary used by all search operations.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Workspace {
    root: PathBuf,
}

impl Workspace {
    /// Opens an existing directory as an allowed workspace.
    pub fn open(path: impl AsRef<Path>) -> Result<Self, WorkspaceError> {
        let requested = path.as_ref();
        let root = requested
            .canonicalize()
            .map_err(|source| WorkspaceError::Unavailable {
                path: requested.to_path_buf(),
                source,
            })?;

        if !root.is_dir() {
            return Err(WorkspaceError::NotDirectory(root));
        }

        Ok(Self { root })
    }

    /// Returns the canonical workspace root.
    pub fn root(&self) -> &Path {
        &self.root
    }

    /// Resolves an existing path and rejects traversal outside the workspace.
    pub fn resolve_existing(&self, path: impl AsRef<Path>) -> Result<PathBuf, WorkspaceError> {
        let requested = path.as_ref();
        let joined = if requested.is_absolute() {
            requested.to_path_buf()
        } else {
            self.root.join(requested)
        };
        let canonical = joined
            .canonicalize()
            .map_err(|source| WorkspaceError::Unavailable {
                path: joined,
                source,
            })?;

        if !canonical.starts_with(&self.root) {
            return Err(WorkspaceError::OutsideBoundary(canonical));
        }

        Ok(canonical)
    }
}

/// Error raised while establishing or enforcing a workspace boundary.
#[derive(Debug)]
pub enum WorkspaceError {
    Unavailable {
        path: PathBuf,
        source: std::io::Error,
    },
    NotDirectory(PathBuf),
    OutsideBoundary(PathBuf),
}

impl fmt::Display for WorkspaceError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Unavailable { path, source } => {
                write!(formatter, "workspace path is unavailable: {}: {source}", path.display())
            }
            Self::NotDirectory(path) => {
                write!(formatter, "workspace path is not a directory: {}", path.display())
            }
            Self::OutsideBoundary(path) => {
                write!(formatter, "path is outside the allowed workspace: {}", path.display())
            }
        }
    }
}

impl std::error::Error for WorkspaceError {}

#[cfg(test)]
mod tests {
    use super::Workspace;

    #[test]
    fn resolve_existing_when_child_exists_returns_canonical_path() {
        let workspace = Workspace::open(env!("CARGO_MANIFEST_DIR")).expect("workspace should open");

        let resolved = workspace
            .resolve_existing("src")
            .expect("child path should resolve");

        assert!(resolved.starts_with(workspace.root()));
        assert!(resolved.ends_with("src"));
    }
}
