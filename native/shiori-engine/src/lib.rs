#![allow(linker_messages)]

use database::{IndexStatus, WorkspaceDatabase};
use serde::Serialize;
use std::ffi::c_void;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::{Path, PathBuf};
use std::slice;

mod database;
mod index;

const ABI_VERSION: u32 = 5;
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

type IndexProgressCallback = unsafe extern "C" fn(
    completed: u64,
    total: u64,
    path: *const u8,
    path_length: usize,
    context: *mut c_void,
);

#[derive(Serialize)]
struct RuntimeDiagnostics {
    abi_version: u32,
    sqlite: database::SqliteDiagnostics,
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

/// # Safety
/// `result` and `error`, when non-null, must point to writable `NativeBuffer` values.
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
        let diagnostics = RuntimeDiagnostics {
            abi_version: ABI_VERSION,
            sqlite: database::sqlite_diagnostics().map_err(|message| (STATUS_IO, message))?,
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

/// # Safety
/// `workspace` must be readable for `workspace_length` bytes. `handle` and `error`,
/// when non-null, must point to writable values.
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

/// # Safety
/// `handle` must be open. Output pointers, when non-null, must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_workspace_info(
    handle: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        let engine = unsafe { engine_and_result(handle, result) }?;
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

/// # Safety
/// `handle` must be open and `query` must be readable for `query_length` bytes.
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
        let query = unsafe { read_utf8(query, query_length) }?
            .trim()
            .to_lowercase();
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
            return Err((
                STATUS_IO,
                "workspace index is not ready; run shiori index build".to_owned(),
            ));
        }
        let matches = engine
            .database
            .search_files(&query, limit)
            .map_err(|message| (STATUS_IO, message))?;
        unsafe { *result = NativeBuffer::from_string(serialize_results(&matches)) };
        Ok(())
    })
}

/// # Safety
/// `handle` must be open. Output pointers, when non-null, must be writable.
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

/// # Safety
/// `handle` must be open and `count` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_directory_count(
    handle: *mut c_void,
    count: *mut u64,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        if handle.is_null() || count.is_null() {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "invalid directory count arguments".to_owned(),
            ));
        }
        let engine = unsafe { &*handle.cast::<Engine>() };
        let value =
            index::count_directories(&engine.root).map_err(|message| (STATUS_IO, message))?;
        unsafe { *count = value };
        Ok(())
    })
}

/// # Safety
/// `handle` must be open. `callback`, when supplied, must remain valid for this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_build(
    handle: *mut c_void,
    total_directories: u64,
    callback: Option<IndexProgressCallback>,
    context: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    ffi_boundary(error, || {
        let engine = unsafe { engine_and_result(handle, result) }?;
        if total_directories == 0 {
            return Err((
                STATUS_INVALID_ARGUMENT,
                "directory count must be greater than zero".to_owned(),
            ));
        }
        let status = engine
            .database
            .build_index(&engine.root, total_directories, |completed, total, path| {
                if let Some(notify) = callback {
                    let bytes = path.as_bytes();
                    unsafe { notify(completed, total, bytes.as_ptr(), bytes.len(), context) };
                }
            })
            .map_err(|message| (STATUS_IO, message))?;
        write_index_status(Ok(status), result)
    })
}

/// # Safety
/// Same requirements as `shiori_engine_index_build`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn shiori_engine_index_rebuild(
    handle: *mut c_void,
    total_directories: u64,
    callback: Option<IndexProgressCallback>,
    context: *mut c_void,
    result: *mut NativeBuffer,
    error: *mut NativeBuffer,
) -> i32 {
    unsafe {
        shiori_engine_index_build(handle, total_directories, callback, context, result, error)
    }
}

/// # Safety
/// `handle` must have been returned by `shiori_engine_open` and not already closed.
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

/// # Safety
/// `buffer` must have been returned by this library and not already freed.
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
    if pointer.is_null() && length > 0 {
        return Err((STATUS_INVALID_ARGUMENT, "input pointer is null".to_owned()));
    }
    let bytes = unsafe { slice::from_raw_parts(pointer, length) };
    std::str::from_utf8(bytes).map_err(|source| {
        (
            STATUS_INVALID_ARGUMENT,
            format!("input is not UTF-8: {source}"),
        )
    })
}

fn serialize_results(results: &[PathBuf]) -> String {
    let mut json = String::from("[");
    for (index, result) in results.iter().enumerate() {
        if index > 0 {
            json.push(',');
        }
        json.push_str("{\"type\":\"file\",\"path\":\"");
        json.push_str(&escape_json(&result.to_string_lossy().replace('\\', "/")));
        json.push_str("\",\"line\":null,\"snippet\":null,\"column\":null}");
    }
    json.push(']');
    json
}

fn escape_json(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n")
        .replace('\r', "\\r")
        .replace('\t', "\\t")
}
