using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Ryder
{
    /// <summary>
    ///   Static class that provides useful helpers to the <see cref="Redirection"/> sub-classes.
    /// </summary>
    [DebuggerStepThrough]
    internal static class Helpers
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

                Array.Copy(BitConverter.GetBytes(destination.ToInt64()), 0, result, 2, 8);

                return result;
            }
            else
            {
                byte[] result = new byte[6];

                result[0] = 0x68;
                result[5] = 0xC3;

                Array.Copy(BitConverter.GetBytes(destination.ToInt32()), 0, result, 1, 4);

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
