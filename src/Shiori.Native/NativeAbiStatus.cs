namespace Shiori.Native;

/// <summary>Exposes native engine diagnostics.</summary>
public static class NativeAbiStatus
{
    /// <summary>Gets the loaded native engine ABI version.</summary>
    public static uint GetAbiVersion() => NativeAbi.GetAbiVersion();
}
