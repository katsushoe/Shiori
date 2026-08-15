#![allow(linker_messages)]

use std::ffi::c_void;
use std::fs;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::{Path, PathBuf};
use std::slice;

const ABI_VERSION: u32 = 1;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_IO: i32 = 2;
const STATUS_PANIC: i32 = 255;
const EXCLUSIONS: &[&str] = &[
    ".git",
    "node_modules",
    "bin",
    "obj",
    "target",
    "dist",
    "build",
    ".vs",
    ".idea",
    ".next",
    "coverage",
    "vendor",
    "packages",
];

struct Engine {
    root: PathBuf,
}

#[repr(C)]
pub struct NativeBuffer {
    pointer: *mut u8,
    length: usize,
}

impl NativeBuffer {
    const EMPTY: Self = Self {
        pointer: std::ptr::null_mut(),
        length: 0,
    };

    fn from_string(value: String) -> Self {
        let bytes = value.into_bytes().into_boxed_slice();
        let length = bytes.len();
        let pointer = Box::into_raw(bytes).cast::<u8>();
        Self { pointer, length }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn shiori_engine_abi_version() -> u32 {
    ABI_VERSION
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_open(
    workspace: *const u8,
    workspace_length: usize,
    handle: *mut *mut c_void,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() {
            return Err((STATUS_INVALID_ARGUMENT, "handle output is null".to_owned()));
        }
        let workspace = unsafe { read_utf8(workspace, workspace_length) }?;
        let root = Path::new(workspace)
            .canonicalize()
            .map_err(|source| (STATUS_IO, format!("workspace is unavailable: {source}")))?;
        if !root.is_dir() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "workspace is not a directory".to_owned(),
            ));
        }
        unsafe { *handle = Box::into_raw(Box::new(Engine { root })).cast() };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_search_files(
    handle: *mut c_void,
    query: *const u8,
    query_length: usize,
    limit: usize,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || result.is_null() || !(1..=100).contains(&limit) {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid search arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let query = unsafe { read_utf8(query, query_length) }?.to_lowercase();
        if query.is_empty() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "query must not be empty".to_owned(),
            ));
        }
        let mut matches = Vec::new();
        walk(&engine.root, &engine.root, &query, limit, &mut matches)
            .map_err(|source| (STATUS_IO, format!("file search failed: {source}")))?;
        unsafe { *result = NativeBuffer::from_string(serialize_results(&matches)) };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_close(handle: *mut c_void) -> bool {
    if handle.is_null() {
        return true;
    }
    catch_unwind(AssertUnwindSafe(|| unsafe {
        drop(Box::from_raw(handle.cast::<Engine>()));
    }))
    .is_ok()
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_free_buffer(buffer: NativeBuffer) {
    if buffer.pointer.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        let slice = std::ptr::slice_from_raw_parts_mut(buffer.pointer, buffer.length);
        drop(Box::from_raw(slice));
    }));
}

fn ffi_boundary(
    error: *mut NativeBuffer,
    operation: impl FnOnce() -> Result<(), (i32, String)>,
) -> i32 {
    if !error.is_null() {
        unsafe { *error = NativeBuffer::EMPTY };
    }
    match catch_unwind(AssertUnwindSafe(operation)) {
        Ok(Ok(())) => 0,
        Ok(Err((status, message))) => {
            set_error(error, message);
            status
        }
        Err(_) => {
            set_error(error, "native engine panicked".to_owned());
            STATUS_PANIC
        }
    }
}

fn set_error(error: *mut NativeBuffer, message: String) {
    if !error.is_null() {
        unsafe { *error = NativeBuffer::from_string(message) };
    }
}

unsafe fn read_utf8<'a>(pointer: *const u8, length: usize) -> Result<&'a str, (i32, String)> {
    if pointer.is_null() {
        return Err((STATUS_INVALID_ARGUMENT, "UTF-8 input is null".to_owned()));
    }
    std::str::from_utf8(unsafe { slice::from_raw_parts(pointer, length) }).map_err(|_| {
        (
            STATUS_INVALID_ARGUMENT,
            "input is not valid UTF-8".to_owned(),
        )
    })
}

fn walk(
    root: &Path,
    directory: &Path,
    query: &str,
    limit: usize,
    results: &mut Vec<PathBuf>,
) -> std::io::Result<()> {
    if results.len() >= limit {
        return Ok(());
    }
    let mut entries = fs::read_dir(directory)?.collect::<Result<Vec<_>, _>>()?;
    entries.sort_by_key(|entry| entry.file_name());
    for entry in entries {
        if results.len() >= limit {
            break;
        }
        let file_type = entry.file_type()?;
        if file_type.is_symlink() {
            continue;
        }
        let path = entry.path();
        if file_type.is_dir() {
            let name = entry.file_name();
            if !EXCLUSIONS
                .iter()
                .any(|excluded| name.to_string_lossy() == *excluded)
            {
                walk(root, &path, query, limit, results)?;
            }
        } else if let Ok(relative) = path.strip_prefix(root)
            && relative.to_string_lossy().to_lowercase().contains(query)
        {
            results.push(relative.to_path_buf());
        }
    }
    Ok(())
}

fn serialize_results(results: &[PathBuf]) -> String {
    let values = results
        .iter()
        .map(|path| {
            format!(
                "{{\"type\":\"file\",\"path\":\"{}\",\"line\":null,\"snippet\":null}}",
                escape_json(&path.to_string_lossy().replace('\\', "/"))
            )
        })
        .collect::<Vec<_>>()
        .join(",");
    format!("{{\"results\":[{values}]}}")
}

fn escape_json(value: &str) -> String {
    let mut escaped = String::with_capacity(value.len());
    for character in value.chars() {
        match character {
            '"' => escaped.push_str("\\\""),
            '\\' => escaped.push_str("\\\\"),
            '\n' => escaped.push_str("\\n"),
            '\r' => escaped.push_str("\\r"),
            '\t' => escaped.push_str("\\t"),
            character if character.is_control() => {
                escaped.push_str(&format!("\\u{:04x}", character as u32))
            }
            character => escaped.push(character),
        }
    }
    escaped
}

#[cfg(test)]
mod tests {
    use super::escape_json;

    #[test]
    fn escape_json_escapes_control_characters() {
        assert_eq!(escape_json("a\"b\\c\n"), "a\\\"b\\\\c\\n");
    }
}
