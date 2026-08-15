using System.Runtime.InteropServices;

namespace Shiori.Native;

internal static partial class NativeAbi
{
    internal const string LibraryName = "shiori_engine";
    internal const uint SupportedAbiVersion = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeBuffer
    {
        internal readonly nint Pointer;
        internal readonly nuint Length;
    }

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_open")]
    internal static unsafe partial int Open(
        byte* workspace,
        nuint workspaceLength,
        out nint handle,
        out NativeBuffer error);

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_search_files")]
    internal static unsafe partial int SearchFiles(
        ShioriEngineHandle handle,
        byte* query,
        nuint queryLength,
        nuint limit,
        out NativeBuffer result,
        out NativeBuffer error);

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_search_text")]
    internal static unsafe partial int SearchText(
        ShioriEngineHandle handle,
        byte* request,
        nuint requestLength,
        out NativeBuffer result,
        out NativeBuffer error);

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_workspace_info")]
    internal static partial int GetWorkspaceInfo(
        ShioriEngineHandle handle,
        out NativeBuffer result,
        out NativeBuffer error);

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_close")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Close(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "shiori_engine_free_buffer")]
    internal static partial void FreeBuffer(NativeBuffer buffer);
}
