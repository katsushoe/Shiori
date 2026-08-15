#![allow(linker_messages)]

use std::ffi::c_void;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::slice;

mod database;
mod index;
mod languages;
mod symbols;
mod text_search;

use database::{IndexStatus, WorkspaceDatabase};
use serde::{Deserialize, Serialize};

const ABI_VERSION: u32 = 1;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_IO: i32 = 2;
const STATUS_PANIC: i32 = 255;
struct Engine {
    root: PathBuf,
    database: WorkspaceDatabase,
}

#[repr(C)]
pub struct NativeBuffer {
    pointer: *mut u8,
    length: usize,
}

#[derive(Serialize)]
struct RuntimeDiagnostics {
    abi_version: u32,
    sqlite: database::SqliteDiagnostics,
    ripgrep_available: bool,
    ripgrep_version: Option<String>,
    tree_sitter_version: &'static str,
    tree_sitter_languages: &'static [&'static str],
}

#[derive(Deserialize)]
struct SymbolSearchRequest {
    query: String,
    kind: Option<String>,
    language: Option<String>,
    path: Option<String>,
    limit: usize,
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
pub unsafe extern "C" fn shiori_engine_diagnostics(
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if result.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "diagnostics result is null".to_owned(),
            ));
        }
        let sqlite = database::sqlite_diagnostics().map_err(|message| (STATUS_IO, message))?;
        languages::validate_parsers().map_err(|message| (STATUS_IO, message))?;
        let ripgrep_output = Command::new("rg").arg("--version").output();
        let (ripgrep_available, ripgrep_version) = match ripgrep_output {
            Ok(output) if output.status.success() => {
                let version = String::from_utf8_lossy(&output.stdout)
                    .lines()
                    .next()
                    .map(str::to_owned);
                (true, version)
            }
            _ => (false, None),
        };
        let diagnostics = RuntimeDiagnostics {
            abi_version: ABI_VERSION,
            sqlite,
            ripgrep_available,
            ripgrep_version,
            tree_sitter_version: languages::PARSER_VERSION,
            tree_sitter_languages: languages::SUPPORTED_LANGUAGES,
        };
        let json = serde_json::to_string(&diagnostics).map_err(|source| {
            (
                STATUS_IO,
                format!("cannot serialize runtime diagnostics: {source}"),
            )
        })?;
        unsafe { *result = NativeBuffer::from_string(json) };
        Ok(())
    })
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
        let database = WorkspaceDatabase::open(&root).map_err(|message| (STATUS_IO, message))?;
        database
            .validate()
            .map_err(|message| (STATUS_IO, message))?;
        unsafe { *handle = Box::into_raw(Box::new(Engine { root, database })).cast() };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_workspace_info(
    handle: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || result.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid workspace info arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let info = engine.database.info();
        let json = format!(
            "{{\"id\":\"{}\",\"path\":\"{}\",\"name\":\"{}\",\"database_path\":\"{}\",\"schema_version\":{}}}",
            escape_json(&info.id),
            escape_json(&info.path),
            escape_json(&info.name),
            escape_json(&info.database_path.replace('\\', "/")),
            info.schema_version
        );
        unsafe { *result = NativeBuffer::from_string(json) };
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
        let status = engine
            .database
            .index_status()
            .map_err(|message| (STATUS_IO, message))?;
        if status.status != "ready" {
            rebuild_index(engine)?;
        }
        let matches = engine
            .database
            .search_files(&query, limit)
            .map_err(|message| (STATUS_IO, message))?;
        unsafe { *result = NativeBuffer::from_string(serialize_results(&matches)) };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_status(
    handle: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        let engine = unsafe { engine_and_result(handle, result) }?;
        write_index_status(engine.database.index_status(), result)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_build(
    handle: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        let engine = unsafe { engine_and_result(handle, result) }?;
        let status = engine
            .database
            .index_status()
            .map_err(|message| (STATUS_IO, message))?;
        let status = if status.status == "ready" {
            incremental_index(engine)?
        } else {
            rebuild_index(engine)?
        };
        write_index_status(Ok(status), result)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_rebuild(
    handle: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        let engine = unsafe { engine_and_result(handle, result) }?;
        let status = rebuild_index(engine)?;
        write_index_status(Ok(status), result)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_search_text(
    handle: *mut c_void,
    request: *const u8,
    request_length: usize,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || result.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid text search arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let request = unsafe { read_utf8(request, request_length) }?;
        let request: text_search::TextSearchRequest =
            serde_json::from_str(request).map_err(|source| {
                (
                    STATUS_INVALID_ARGUMENT,
                    format!("invalid text search request: {source}"),
                )
            })?;
        let response =
            text_search::search(&engine.root, &request).map_err(|message| (STATUS_IO, message))?;
        let json = serde_json::to_string(&response).map_err(|source| {
            (
                STATUS_IO,
                format!("cannot serialize text search response: {source}"),
            )
        })?;
        unsafe { *result = NativeBuffer::from_string(json) };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_search_symbols(
    handle: *mut c_void,
    request: *const u8,
    request_length: usize,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || result.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid symbol search arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let request = unsafe { read_utf8(request, request_length) }?;
        let request: SymbolSearchRequest = serde_json::from_str(request).map_err(|source| {
            (
                STATUS_INVALID_ARGUMENT,
                format!("invalid symbol search request: {source}"),
            )
        })?;
        if request.query.trim().is_empty() || !(1..=100).contains(&request.limit) {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "query must not be empty and limit must be from 1 to 100".to_owned(),
            ));
        }
        ensure_index(engine)?;
        let response = engine
            .database
            .search_symbols(
                request.query.trim(),
                request.kind.as_deref(),
                request.language.as_deref(),
                request.path.as_deref(),
                request.limit,
            )
            .map_err(|message| (STATUS_IO, message))?;
        let json = serde_json::to_string(&response).map_err(|source| {
            (
                STATUS_IO,
                format!("cannot serialize symbol search response: {source}"),
            )
        })?;
        unsafe { *result = NativeBuffer::from_string(json) };
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_file_outline(
    handle: *mut c_void,
    path: *const u8,
    path_length: usize,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || result.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid file outline arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let requested = unsafe { read_utf8(path, path_length) }?;
        if requested.is_empty() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "outline path must not be empty".to_owned(),
            ));
        }
        let relative_path = resolve_workspace_file(&engine.root, requested)?;
        ensure_index(engine)?;
        let outline = engine
            .database
            .file_outline(&relative_path)
            .map_err(|message| (STATUS_IO, message))?;
        let json = serde_json::to_string(&outline).map_err(|source| {
            (
                STATUS_IO,
                format!("cannot serialize file outline: {source}"),
            )
        })?;
        unsafe { *result = NativeBuffer::from_string(json) };
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

unsafe fn engine_and_result<'a>(
    handle: *mut c_void,
    result: *mut NativeBuffer,
) -> Result<&'a Engine, (i32, String)> {
    if handle.is_null() || result.is_null() {
        return Err((
            STATUS_INVALID_ARGUMENT,
            "invalid index operation arguments".to_owned(),
        ));
    }
    Ok(unsafe { &*handle.cast::<Engine>() })
}

fn rebuild_index(engine: &Engine) -> Result<IndexStatus, (i32, String)> {
    let files = index::scan(&engine.root).map_err(|message| (STATUS_IO, message))?;
    engine
        .database
        .replace_file_index(&files)
        .map_err(|message| (STATUS_IO, message))?;
    engine
        .database
        .index_status()
        .map_err(|message| (STATUS_IO, message))
}

fn incremental_index(engine: &Engine) -> Result<IndexStatus, (i32, String)> {
    let previous = engine
        .database
        .file_fingerprints()
        .map_err(|message| (STATUS_IO, message))?;
    let scan =
        index::scan_incremental(&engine.root, &previous).map_err(|message| (STATUS_IO, message))?;
    engine
        .database
        .apply_incremental_index(&scan)
        .map_err(|message| (STATUS_IO, message))?;
    engine
        .database
        .index_status()
        .map_err(|message| (STATUS_IO, message))
}

fn ensure_index(engine: &Engine) -> Result<(), (i32, String)> {
    let status = engine
        .database
        .index_status()
        .map_err(|message| (STATUS_IO, message))?;
    if status.status != "ready" {
        rebuild_index(engine)?;
    }
    Ok(())
}

fn resolve_workspace_file(root: &Path, requested: &str) -> Result<String, (i32, String)> {
    let path = root
        .join(requested)
        .canonicalize()
        .map_err(|source| (STATUS_IO, format!("outline file is unavailable: {source}")))?;
    if !path.starts_with(root) || !path.is_file() {
        return Err((
            STATUS_INVALID_ARGUMENT,
            "outline path must be a file inside the workspace".to_owned(),
        ));
    }
    path.strip_prefix(root)
        .map(|relative| relative.to_string_lossy().replace('\\', "/"))
        .map_err(|source| {
            (
                STATUS_INVALID_ARGUMENT,
                format!("cannot resolve outline path: {source}"),
            )
        })
}

fn write_index_status(
    status: Result<IndexStatus, String>,
    result: *mut NativeBuffer,
) -> Result<(), (i32, String)> {
    let status = status.map_err(|message| (STATUS_IO, message))?;
    let json = serde_json::to_string(&status).map_err(|source| {
        (
            STATUS_IO,
            format!("cannot serialize index status: {source}"),
        )
    })?;
    unsafe { *result = NativeBuffer::from_string(json) };
    Ok(())
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
