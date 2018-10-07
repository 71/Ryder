using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryder.Lightweight
{
    
    /// <summary>
    ///   Provides the ability to redirect calls from one method to another.
    /// </summary>
    internal sealed class Redirection : IDisposable
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

        internal Redirection(MethodBase original, MethodBase replacement, bool start)
        {
            Original = original;
            Replacement = replacement;

            // Note: I'm making local copies of the following fields to avoid accessing fields multiple times.
            RuntimeMethodHandle originalHandle = Helpers.GetRuntimeMethodHandle(original);
            RuntimeMethodHandle replacementHandle = Helpers.GetRuntimeMethodHandle(replacement);

            const string JIT_ERROR = "The specified method hasn't been jitted yet, and thus cannot be used in a redirection.";

            // Fetch their respective start
            IntPtr originalStart = Helpers.GetMethodStart(originalHandle);
            IntPtr replacementStart = Helpers.GetMethodStart(replacementHandle);

            // Edge case: calling this on the same method
            if (originalStart == replacementStart)
                throw new InvalidOperationException("Cannot redirect a method to itself.");

            // Edge case: methods are too close to one another
            int difference = (int)Math.Abs(originalStart.ToInt64() - replacementStart.ToInt64());
            int sizeOfPtr = Marshal.SizeOf<IntPtr>();

            if (difference <= Helpers.PatchSize)
                throw new InvalidOperationException("Unable to redirect methods whose bodies are too close to one another.");

            // Make sure they're jitted
            if (!Helpers.HasBeenCompiled(originalStart))
            {
                if (!Helpers.TryPrepareMethod(original, originalHandle))
                    throw new ArgumentException(JIT_ERROR, nameof(original));

                originalStart = Helpers.GetMethodStart(originalHandle);
            }

            if (!Helpers.HasBeenCompiled(replacementStart))
            {
                if (!Helpers.TryPrepareMethod(replacement, replacementHandle))
                    throw new ArgumentException(JIT_ERROR, nameof(replacement));

                replacementStart = Helpers.GetMethodStart(replacementHandle);
            }

            // Copy local value to field
            originalMethodStart = originalStart;

            // In some cases, the memory might need to be readable / writable:
            // Make the memory region rw right away just in case.
            Helpers.AllowRW(originalStart);

            // Save bytes to change to redirect method
            byte[] replBytes = replacementBytes = Helpers.GetJmpBytes(replacementStart);
            byte[] origBytes = originalBytes = new byte[replBytes.Length];

            Marshal.Copy(originalStart, origBytes, 0, origBytes.Length);

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
            private const BindingFlags PUBLIC_STATIC = BindingFlags.Public | BindingFlags.Static;
            private const BindingFlags PUBLIC_INSTANCE = BindingFlags.Public | BindingFlags.Instance;
            private const BindingFlags ALL_INSTANCE = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            private const BindingFlags NON_PUBLIC_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

            internal static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            internal static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            internal static readonly bool IsARM;
            internal static readonly int PatchSize;

            private static Exception UnsupportedArchitecture => new PlatformNotSupportedException("Architecture not supported.");

            static Helpers()
            {
                switch (RuntimeInformation.ProcessArchitecture)
                {
                    case Architecture.Arm:
                        PatchSize = 8;
                        IsARM = true;
                        break;
                    case Architecture.Arm64:
                        PatchSize = 12;
                        IsARM = true;
                        break;
                    case Architecture.X86:
                        PatchSize = 6;
                        IsARM = false;
                        break;
                    case Architecture.X64:
                        PatchSize = 12;
                        IsARM = false;
                        break;
                }
            }

            private static readonly Func<Type, object> GetUninitializedObject
                = typeof(RuntimeHelpers)
                    .GetMethod(nameof(GetUninitializedObject), PUBLIC_STATIC)?
                    .CreateDelegate(typeof(Func<Type, object>)) as Func<Type, object>;

            private static readonly Action<RuntimeMethodHandle> PrepareMethod
                = typeof(RuntimeHelpers)
                    .GetMethod(nameof(PrepareMethod), new[] { typeof(RuntimeMethodHandle) })?
                    .CreateDelegate(typeof(Action<RuntimeMethodHandle>)) as Action<RuntimeMethodHandle>;

            #region Emitted IL
            private static Func<DynamicMethod, RuntimeMethodHandle> FindMethodHandleOfDynamicMethod;

            private static Func<DynamicMethod, RuntimeMethodHandle> MakeFindMethodHandleOfDynamicMethod()
            {
                // Generate the "FindMethodHandleOfDynamicMethod" delegate.
                DynamicMethod findMethodHandle = new DynamicMethod(
                    nameof(FindMethodHandleOfDynamicMethod),
                    typeof(RuntimeMethodHandle),
                    new[] { typeof(DynamicMethod) },
                    typeof(DynamicMethod), true);

                ILGenerator il = findMethodHandle.GetILGenerator(16);

                il.Emit(OpCodes.Ldarg_0);

                MethodInfo getMethodDescriptor = typeof(DynamicMethod)
                    .GetMethod("GetMethodDescriptor", NON_PUBLIC_INSTANCE);

                if (getMethodDescriptor != null)
                {
                    il.Emit(OpCodes.Callvirt, getMethodDescriptor);
                }
                else
                {
                    FieldInfo handleField = typeof(DynamicMethod).GetField("m_method", NON_PUBLIC_INSTANCE)
                                         ?? typeof(DynamicMethod).GetField("mhandle", NON_PUBLIC_INSTANCE)
                                         ?? typeof(DynamicMethod).GetField("m_methodHandle", NON_PUBLIC_INSTANCE);

                    il.Emit(OpCodes.Ldfld, handleField);
                }

                il.Emit(OpCodes.Ret);

                return findMethodHandle.CreateDelegate(typeof(Func<DynamicMethod, RuntimeMethodHandle>))
                    as Func<DynamicMethod, RuntimeMethodHandle>;
            }

            private static Func<MethodBase, RuntimeMethodHandle> GetMethodHandle;

            private static Func<MethodBase, RuntimeMethodHandle> MakeGetMethodHandle()
            {
                // Generate the "GetMethodHandle" delegate.
                DynamicMethod getMethodHandle = new DynamicMethod(
                    nameof(GetMethodHandle),
                    typeof(RuntimeMethodHandle),
                    new[] { typeof(MethodBase) },
                    typeof(RuntimeMethodHandle), true);

                ILGenerator il = getMethodHandle.GetILGenerator(16);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetProperty("MethodHandle", PUBLIC_INSTANCE).GetMethod);
                il.Emit(OpCodes.Ret);

                return getMethodHandle.CreateDelegate(typeof(Func<MethodBase, RuntimeMethodHandle>))
                    as Func<MethodBase, RuntimeMethodHandle>;
            }

            private static Func<RuntimeMethodHandle, IntPtr> GetFunctionPointer;

            private static Func<RuntimeMethodHandle, IntPtr> MakeGetFunctionPointer()
            {
                // Generate the "GetFunctionPointer" delegate.
                DynamicMethod getFunctionPointer = new DynamicMethod(
                    nameof(GetFunctionPointer),
                    typeof(IntPtr),
                    new[] { typeof(RuntimeMethodHandle) },
                    typeof(RuntimeMethodHandle), true);

                ILGenerator il = getFunctionPointer.GetILGenerator(16);

                il.Emit(OpCodes.Ldarga_S, (short)0);
                il.Emit(OpCodes.Call, typeof(RuntimeMethodHandle).GetMethod(nameof(GetFunctionPointer), PUBLIC_INSTANCE));
                il.Emit(OpCodes.Ret);

                return getFunctionPointer.CreateDelegate(typeof(Func<RuntimeMethodHandle, IntPtr>))
                    as Func<RuntimeMethodHandle, IntPtr>;
            }
            #endregion

            /// <summary>
            ///   Returns a <see cref="byte"/> array that corresponds to asm instructions
            ///   of a JMP to the <paramref name="destination"/> pointer.
            /// </summary>
            public static byte[] GetJmpBytes(IntPtr destination)
            {
                switch (RuntimeInformation.ProcessArchitecture)
                {
                    case Architecture.Arm:
                        {
                            // LDR PC, [PC, #-4]
                            // $addr
                            byte[] result = new byte[8];

                            result[0] = 0x04;
                            result[1] = 0xF0;
                            result[2] = 0x1F;
                            result[3] = 0xE5;

                            BitConverter.GetBytes(destination.ToInt32()).CopyTo(result, 4);

                            return result;
                        }

                    case Architecture.Arm64:
                        {
                            // LDR PC, [PC, #-4]
                            // $addr
                            byte[] result = new byte[12];

                            result[0] = 0x04;
                            result[1] = 0xF0;
                            result[2] = 0x1F;
                            result[3] = 0xE5;

                            BitConverter.GetBytes(destination.ToInt64()).CopyTo(result, 4);

                            return result;
                        }

                    case Architecture.X64:
                        {
                            // movabs rax,$addr
                            // jmp rax
                            byte[] result = new byte[12];

                            result[0] = 0x48;
                            result[1] = 0xB8;
                            result[10] = 0xFF;
                            result[11] = 0xE0;

                            BitConverter.GetBytes(destination.ToInt64()).CopyTo(result, 2);

                            return result;
                        }

                    case Architecture.X86:
                        {
                            // push $addr
                            // ret
                            byte[] result = new byte[6];

                            result[0] = 0x68;
                            result[5] = 0xC3;

                            BitConverter.GetBytes(destination.ToInt32()).CopyTo(result, 1);

                            return result;
                        }

                    default:
                        throw UnsupportedArchitecture;
                }
            }

            /// <summary>
            ///   Returns the <see cref="RuntimeMethodHandle"/> corresponding to the specified <paramref name="method"/>.
            /// </summary>
            public static RuntimeMethodHandle GetRuntimeMethodHandle(MethodBase method)
            {
                if (method is DynamicMethod dynamicMethod)
                {
                    var findMethodHandle = FindMethodHandleOfDynamicMethod
                                        ?? (FindMethodHandleOfDynamicMethod = MakeFindMethodHandleOfDynamicMethod());

                    return findMethodHandle(dynamicMethod);
                }

                var getMethodHandle = GetMethodHandle
                                   ?? (GetMethodHandle = MakeGetMethodHandle());

                return getMethodHandle(method);
            }

            /// <summary>
            ///   Returns an <see cref="IntPtr"/> pointing to the start of the method's jitted body.
            /// </summary>
            public static IntPtr GetMethodStart(RuntimeMethodHandle handle)
            {
                var getFunctionPointer = GetFunctionPointer
                                      ?? (GetFunctionPointer = MakeGetFunctionPointer());

                return getFunctionPointer(handle);
            }

            /// <summary>
            ///   Attempts to run the specified <paramref name="method"/> through the JIT compiler,
            ///   avoiding some unexpected behavior related to an uninitialized method.
            /// </summary>
            public static bool TryPrepareMethod(MethodBase method, RuntimeMethodHandle handle)
            {
                // First, try the good ol' RuntimeHelpers.PrepareMethod.
                if (PrepareMethod != null)
                {
                    PrepareMethod(handle);
                    return true;
                }

                // No chance, we gotta go lower.
                // Invoke the method with uninitialized arguments.
                object sender = null;

                object[] GetArguments(ParameterInfo[] parameters)
                {
                    object[] args = new object[parameters.Length];

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        ParameterInfo param = parameters[i];

                        if (param.HasDefaultValue)
                            args[i] = param.DefaultValue;
                        else if (param.ParameterType.GetTypeInfo().IsValueType)
                            args[i] = Activator.CreateInstance(param.ParameterType);
                        else
                            args[i] = null;
                    }

                    return args;
                }

                if (!method.IsStatic)
                {
                    // Gotta make the instance
                    Type declaringType = method.DeclaringType;

                    if (declaringType.GetTypeInfo().IsValueType)
                    {
                        sender = Activator.CreateInstance(declaringType);
                    }
                    else if (declaringType.GetTypeInfo().IsAbstract)
                    {
                        // Overkill solution: Find a type in the assembly that implements the declaring type,
                        // and use it instead.
                        throw new InvalidOperationException("Cannot manually JIT a method");
                    }
                    else if (GetUninitializedObject != null)
                    {
                        sender = GetUninitializedObject(declaringType);
                    }
                    else
                    {
                        /* TODO
                         * Since I just made the whole 'gotta JIT the method' step mandatory
                         * in the MethodRedirection ctor, i should make sure this always returns true.
                         * That means looking up every type for overriding types for the throwing step above,
                         * and testing every possible constructor to create the instance.
                         * 
                         * Additionally, if we want to go even further, we can repeat this step for every
                         * single argument of the ctor, thus making sure that we end up having an actual class.
                         * In this case, unless the user wants to instantiate an abstract class with no overriding class,
                         * everything'll work. HOWEVER, performances would be less-than-ideal. A simple Redirection
                         * may mean scanning the assembly a dozen times for overriding types, calling their constructors
                         * hundreds of times, knowing that all of them will be slow (Reflection + Try/Catch blocks aren't
                         * perfs-friendly).
                         */
                        ConstructorInfo ctor = declaringType.GetConstructor(Type.EmptyTypes);

                        if (ctor != null)
                        {
                            sender = ctor.Invoke(null);
                        }
                        else
                        {
                            ConstructorInfo[] ctors = declaringType.GetConstructors(ALL_INSTANCE);

                            Array.Sort(ctors, (a, b) => a.GetParameters().Length.CompareTo(b.GetParameters().Length));

                            ctor = ctors[0];

                            try
                            {
                                sender = ctor.Invoke(GetArguments(ctor.GetParameters()));
                            }
                            catch (TargetInvocationException)
                            {
                                // Nothing we can do, give up.
                                return false;
                            }
                        }
                    }
                }

                try
                {
                    method.Invoke(sender, GetArguments(method.GetParameters()));
                }
                catch (TargetInvocationException)
                {
                    // That's okay.
                }

                return true;
            }

            /// <summary>
            ///   Returns whether or not the specified <paramref name="methodStart"/> has
            ///   already been compiled by the JIT.
            /// </summary>
            public static unsafe bool HasBeenCompiled(IntPtr methodStart)
            {
                // Yes this function is unsafe, sorry. If anyone knows how to bitcast longs to ulongs, then I'll take it.
                // Note: this code has only been tested on x86_64. If anyone can confirm it works on ARM / ARM64 / i386, that'd be great.

                switch (RuntimeInformation.ProcessArchitecture)
                {
                    // According to this:
                    //   https://github.com/dotnet/coreclr/blob/master/Documentation/botr/method-descriptor.md
                    // Uncompiled methods will have have specific 'stubs' bodies with common patterns.
                    //
                    // Therefore, we simply want to know if any of these patterns can be seen.
                    //
                    // Note that theve values change depending on the architecture.
                    case Architecture.X64:
                        {
                            ushort* code = (ushort*)methodStart;

                            if (code[0] == 0xBA49)
                                // Stub precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L502
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L553
                                return false;

                            goto i386Common;
                        }

                    case Architecture.X86:
                        {
                            byte* code = (byte*)methodStart;

                            if (code[5] == 0xB8)
                                // Stub precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L502
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L555
                                return false;

                            goto i386Common;
                        }

                    i386Common:
                        {
                            byte* code = (byte*)methodStart;

                            if (code[0] == 0xE9)
                                // Fixup precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L723
                                return false;

                            return true;
                        }

                    case Architecture.Arm:
                        {
                            ushort* code = (ushort*)methodStart;

                            if (code[0] == 0xf8df && code[1] == 0xc008 && code[2] == 0xf8df && code[3] == 0xf000)
                                // Stub precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/arm/stubs.cpp#L714
                                return false;

                            if (code[0] == 0x46fc && code[1] == 0xf8df && code[2] == 0xf004)
                                // Fixup precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/arm/stubs.cpp#L782
                                return false;

                            return true;
                        }

                    case Architecture.Arm64:
                        {
                            uint* code = (uint*)methodStart;

                            if (code[0] == 0x10000089 && code[1] == 0xA940312A && code[2] == 0xD61F0140)
                                // Stub precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/arm64/stubs.cpp#L573
                                return false;

                            if (code[0] == 0x1000000C && code[1] == 0x5800006B && code[2] == 0xD61F0160)
                                // Fixup precode:
                                //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/arm64/stubs.cpp#L639
                                //   https://github.com/dotnet/coreclr/blob/30f0be906507bef99d951efc5eb9a1664bde9ddd/src/vm/arm64/cgencpu.h#L674
                                return false;

                            return true;
                        }

                    default:
                        throw UnsupportedArchitecture;
                }
            }

            private const string LIBSYSTEM = "libSystem.dylib";
            private const string KERNEL32 = "kernel32.dll";
            private const string LIBC = "libc.so.6";

            [DllImport(KERNEL32)]
            internal static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, int flNewProtect, out int lpflOldProtect);

            [DllImport(LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true, EntryPoint = "mprotect")]
            internal static extern int LinuxProtect(IntPtr start, ulong len, int prot);

            [DllImport(LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true, EntryPoint = "getpagesize")]
            internal static extern long LinuxGetPageSize();

            [DllImport(LIBSYSTEM, CallingConvention = CallingConvention.Cdecl, SetLastError = true, EntryPoint = "mprotect")]
            internal static extern int OsxProtect(IntPtr start, ulong len, int prot);

            [DllImport(LIBSYSTEM, CallingConvention = CallingConvention.Cdecl, SetLastError = true, EntryPoint = "getpagesize")]
            internal static extern long OsxGetPageSize();

            internal static void AllowRW(IntPtr address)
            {
                if (IsARM)
                    return;

                if (IsWindows)
                {
                    if (VirtualProtect(address, new UIntPtr(1), 0x40 /* PAGE_EXECUTE_READWRITE */, out var _))
                        return;

                    goto Error;
                }

                long pagesize = IsLinux ? LinuxGetPageSize() : OsxGetPageSize();
                long start = address.ToInt64();
                long pagestart = start & -pagesize;

                int buffsize = IntPtr.Size == sizeof(int) ? 6 : 12;

                if (IsLinux && LinuxProtect(new IntPtr(pagestart), (ulong)(start + buffsize - pagestart), 0x7 /* PROT_READ_WRITE_EXEC */) == 0)
                    return;
                if (!IsLinux && OsxProtect(new IntPtr(pagestart), (ulong)(start + buffsize - pagestart), 0x7 /* PROT_READ_WRITE_EXEC */) == 0)
                    return;

                Error:
                throw new Exception($"Unable to make method memory readable and writable. Error code: {Marshal.GetLastWin32Error()}");
            }
        }
    }
}