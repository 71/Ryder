#if NET45
using System;

namespace System.ComponentModel
{
    internal sealed class EditorBrowsableAttribute : Attribute
    {
        internal EditorBrowsableAttribute(EditorBrowsableState state) : base()
        {
        }
    }

    internal enum EditorBrowsableState
    {
        Never
    }
}

internal enum OSPlatform
{
    Linux,
    Windows,
}

internal enum Architecture
{
    Arm,
    Arm64,
    X64,
    X86,
}

internal static class RuntimeInformation
{
    internal static Architecture ProcessArchitecture => IntPtr.Size == 4 ? Architecture.X86 : Architecture.X64;
    internal static bool IsOSPlatform(OSPlatform platform) => platform == OSPlatform.Windows;
}
#endif
