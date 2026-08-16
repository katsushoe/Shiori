use std::path::{Path, PathBuf};
use std::process::Command;

pub(crate) fn command() -> Command {
    Command::new(executable_path())
}

fn executable_path() -> PathBuf {
    let current_executable = std::env::current_exe().ok();
    resolve_executable(current_executable.as_deref())
}

fn resolve_executable(current_executable: Option<&Path>) -> PathBuf {
    if let Some(directory) = current_executable.and_then(Path::parent) {
        let bundled = directory.join(executable_name());
        if bundled.is_file() {
            return bundled;
        }
    }

    PathBuf::from("rg")
}

fn executable_name() -> &'static str {
    if cfg!(windows) { "rg.exe" } else { "rg" }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resolve_executable_prefers_sibling_binary() {
        let directory = std::env::temp_dir().join(format!(
            "shiori-ripgrep-test-{}",
            std::process::id()
        ));
        std::fs::create_dir_all(&directory).expect("create test directory");
        let bundled = directory.join(executable_name());
        std::fs::write(&bundled, []).expect("create bundled ripgrep stub");

        let result = resolve_executable(Some(&directory.join("shiori.exe")));

        assert_eq!(bundled, result);
        std::fs::remove_dir_all(directory).expect("remove test directory");
    }

    #[test]
    fn resolve_executable_falls_back_to_path() {
        let result = resolve_executable(None);

        assert_eq!(PathBuf::from("rg"), result);
    }
}
