using System;
using System.Collections.Generic;
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

    internal Redirection(MethodBase original, MethodBase replacement, bool start)
    {
        Original = original;
        Replacement = replacement;

        // Note: I'm making local copies of the following fields to avoid accessing fields multiple times.
        RuntimeMethodHandle originalHandle = Helpers.GetRuntimeMethodHandle(original);
        RuntimeMethodHandle replacementHandle = Helpers.GetRuntimeMethodHandle(replacement);

        const string JittedError = "The specified method hasn't been jitted yet, and thus cannot be used in a redirection.";

        // Fetch their respective start
        IntPtr originalStart = Helpers.GetMethodStart(originalHandle);
        IntPtr replacementStart = Helpers.GetMethodStart(replacementHandle);

        // Edge case: calling this on the same method
        if (originalStart == replacementStart)
            throw new InvalidOperationException("Cannot redirect a method to itself.");

        // Make sure they're jitted
        if (!Helpers.HasBeenCompiled(originalStart))
        {
            if (!Helpers.TryPrepareMethod(original, originalHandle))
                throw new ArgumentException(JittedError, nameof(original));

            originalStart = Helpers.GetMethodStart(originalHandle);
        }

        if (!Helpers.HasBeenCompiled(replacementStart))
        {
            if (!Helpers.TryPrepareMethod(replacement, replacementHandle))
                throw new ArgumentException(JittedError, nameof(replacement));

            replacementStart = Helpers.GetMethodStart(replacementHandle);
        }

        // Copy local value to field
        originalMethodStart = originalStart;

        // Make sure the memory is readable on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Helpers.VirtualProtect(originalStart, new UIntPtr(1), 0x40 /* PAGE_EXECUTE_READWRITE */, out var _);

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
        public static bool HasBeenCompiled(IntPtr methodStart)
        {
            // According to this:
            //   https://github.com/dotnet/coreclr/blob/master/Documentation/botr/method-descriptor.md
            // An uncompiled method will look like
            //    call ...
            //    pop esi
            //    dword ...
            // In x64, that's
            //    0xE8 <short>
            //    ...
            //    0x5F 0x5E
            //
            // According to this:
            //   https://github.com/dotnet/coreclr/blob/aff5a085543f339a24a5e58f37c1641394155c45/src/vm/i386/stublinkerx86.h#L660
            // 0x5F and 0x5E below are constants...
            // According to these:
            //   http://ref.x86asm.net/coder64.html#xE8, http://ref.x86asm.net/coder32.html#xE8
            // CALL <rel32> is the same byte on both x86 and x64, so we should be good.
            //
            // Would be nice to try this on x86 though.

            const int ANALYZED_FIXUP_SIZE = 6;
            byte[] buffer = new byte[ANALYZED_FIXUP_SIZE];

            Marshal.Copy(methodStart, buffer, 0, ANALYZED_FIXUP_SIZE);

            return buffer[0] != 0xE8 || buffer[4] != 0x5F || buffer[5] != 0x5E;
        }

        /// <summary>
        ///   Changes the protection of a memory region.
        /// </summary>
        [DllImport("kernel32.dll")]
        public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, int flNewProtect, out int lpflOldProtect);
    }
}