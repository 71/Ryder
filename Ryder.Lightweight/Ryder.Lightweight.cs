using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///   Provides the ability to redirect calls from one method to another.
/// </summary>
internal sealed class Redirection
{
    /// <summary>
    ///   Methods to reference statically to prevent them from being
    ///   garbage-collected.
    /// </summary>
    private static readonly List<MethodBase> PersistingMethods = new List<MethodBase>();

    private readonly byte[] originalBytes;
    private readonly byte[] replacementBytes;

    private readonly IntPtr originalMethodStart;

    /// <summary>
    ///   Gets the original <see cref="MethodBase"/>.
    /// </summary>
    public MethodBase Original { get; }

    /// <summary>
    ///   Gets the replacing <see cref="MethodBase"/>.
    /// </summary>
    public MethodBase Replacement { get; }

    internal Redirection(MethodBase original, MethodBase replacement, bool start = false)
    {
        Original = original;
        Replacement = replacement;

        // Note: I'm making local copies of the following fields to avoid accessing fields multiple times.
        IntPtr originalStart = originalMethodStart = Helpers.GetMethodStart(original);
        IntPtr replacementStart = Helpers.GetMethodStart(replacement);

        // Edge case: calling this on the same method
        if (originalStart == replacementStart)
            throw new InvalidOperationException("Cannot redirect a method to itself.");

        // Make sure the memory is readable on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Helpers.VirtualProtect(originalStart, new UIntPtr(1), 0x40 /* PAGE_EXECUTE_READWRITE */, out var _);

        // Save bytes to change to redirect method
        byte[] replBytes = replacementBytes = Helpers.GetJmpBytes(replacementStart);
        byte[] origBytes = originalBytes = new byte[replBytes.Length];

        Marshal.Copy(originalStart, origBytes, 0, origBytes.Length);

        //Debug.Assert(originalStart.ToInt64() + replBytes.Length < replacementStart.ToInt64(),
        //             "The original method overlaps on the replacement method; calling the latter will break things.");

        if (start)
        {
            CopyToStart(replBytes, originalStart);
            isRedirecting = true;
        }

        // Save methods in static array to make sure they're not garbage collected
        PersistingMethods.Add(original);
        PersistingMethods.Add(replacement);
    }

    /// <summary>
    ///   Starts redirecting calls to the replacing <see cref="MethodBase"/>.
    /// </summary>
    public void Start()
    {
        if (isRedirecting)
            return;

        CopyToStart(replacementBytes, originalMethodStart);

        isRedirecting = true;
    }

    /// <summary>
    ///   Stops redirecting calls to the replacing <see cref="MethodBase"/>.
    /// </summary>
    public void Stop()
    {
        if (!isRedirecting)
            return;

        CopyToStart(originalBytes, originalMethodStart);

        isRedirecting = false;
    }

    /// <summary>
    ///   Invokes the original method, no matter the current redirection state.
    /// </summary>
    public object InvokeOriginal(object obj, params object[] args)
    {
        IntPtr methodStart = originalMethodStart;
        bool wasRedirecting = isRedirecting;

        if (wasRedirecting)
            CopyToStart(originalBytes, methodStart);

        try
        {
            if (obj == null && Original.IsConstructor)
                return ((ConstructorInfo)Original).Invoke(args);

            return Original.Invoke(obj, args);
        }
        finally
        {
            if (wasRedirecting)
                CopyToStart(replacementBytes, methodStart);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();

        PersistingMethods.Remove(Original);
        PersistingMethods.Remove(Replacement);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyToStart(byte[] bytes, IntPtr methodStart) => Marshal.Copy(bytes, 0, methodStart, bytes.Length);

    bool isRedirecting;

    private static class Helpers
    {
        private static Func<DynamicMethod, RuntimeMethodHandle> FindMethodHandle;

        private static Func<DynamicMethod, RuntimeMethodHandle> MakeFindMethodHandle()
        {
            // Generate the "FindMethodHandle" delegate.
#if NETSTANDARD_1_5
            const BindingFlags NON_PUBLIC_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
#endif

            DynamicMethod findMethodHandle = new DynamicMethod(
                nameof(FindMethodHandle),
                typeof(RuntimeMethodHandle),
                new[] { typeof(DynamicMethod) });

            ILGenerator il = findMethodHandle.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            MethodInfo getMethodDescriptor = typeof(DynamicMethod)
#if NETSTANDARD_1_5
                .GetMethod("GetMethodDescriptor", NON_PUBLIC_INSTANCE);
#else
                .GetRuntimeMethod("GetMethodDescriptor", Type.EmptyTypes);
#endif

            if (getMethodDescriptor != null)
            {
                il.Emit(OpCodes.Callvirt, getMethodDescriptor);
            }
            else
            {
#if NETSTANDARD_1_5
                FieldInfo handleField = typeof(DynamicMethod).GetField("m_method", NON_PUBLIC_INSTANCE)
                                     ?? typeof(DynamicMethod).GetField("mhandle", NON_PUBLIC_INSTANCE);
#else
                FieldInfo handleField = typeof(DynamicMethod).GetRuntimeField("m_method")
                                     ?? typeof(DynamicMethod).GetRuntimeField("mhandle");
#endif

                il.Emit(OpCodes.Ldfld, handleField);
            }

            il.Emit(OpCodes.Ret);

            return (Func<DynamicMethod, RuntimeMethodHandle>)findMethodHandle.CreateDelegate(typeof(Func<DynamicMethod, RuntimeMethodHandle>));
        }

        public static byte[] GetJmpBytes(IntPtr destination)
        {
            if (IntPtr.Size == sizeof(long))
            {
                byte[] result = new byte[12];

                result[0] = 0x48;
                result[1] = 0xB8;
                result[10] = 0xFF;
                result[11] = 0xE0;

                BitConverter.GetBytes(destination.ToInt64()).CopyTo(result, 2);

                return result;
            }
            else
            {
                byte[] result = new byte[6];

                result[0] = 0x68;
                result[5] = 0xC3;

                BitConverter.GetBytes(destination.ToInt32()).CopyTo(result, 1);

                return result;
            }
        }

        private static RuntimeMethodHandle GetRuntimeMethodHandle(MethodBase method)
        {
            if (method is DynamicMethod dynamicMethod)
            {
                var findMethodHandle = FindMethodHandle ?? (FindMethodHandle = MakeFindMethodHandle());

                return findMethodHandle(dynamicMethod);
            }

            return (RuntimeMethodHandle)typeof(MethodBase)
#if NETSTANDARD_1_5
                .GetProperty("MethodHandle", BindingFlags.Instance | BindingFlags.Public)
#else
                .GetRuntimeProperty("MethodHandle")
#endif
                .GetValue(method);
        }

        public static IntPtr GetMethodStart(MethodBase method)
        {
            var handle = GetRuntimeMethodHandle(method);

            return (IntPtr)typeof(RuntimeMethodHandle)
#if NETSTANDARD_1_5
                .GetMethod("GetFunctionPointer", BindingFlags.Instance | BindingFlags.Public)
#else
                .GetRuntimeMethod("GetFunctionPointer", Type.EmptyTypes)
#endif
                .Invoke(handle, null);
        }

        [DllImport("kernel32.dll")]
        public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, int flNewProtect, out int lpflOldProtect);
    }
}