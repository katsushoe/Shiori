using Microsoft.Win32.SafeHandles;

namespace Shiori.Native;

internal sealed class ShioriEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal ShioriEngineHandle(nint handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeAbi.Close(handle);
}
