using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryder
{
    /// <summary>
    ///   Class that provides full control over a method <see cref="Redirection"/>.
    /// </summary>
    public sealed class MethodRedirection : Redirection
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

        internal MethodRedirection(MethodBase original, MethodBase replacement, bool start = false)
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
        public override void Start()
        {
            if (isRedirecting)
                return;

            CopyToStart(replacementBytes, originalMethodStart);

            isRedirecting = true;
        }

        /// <summary>
        ///   Stops redirecting calls to the replacing <see cref="MethodBase"/>.
        /// </summary>
        public override void Stop()
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
        public override void Dispose()
        {
            Stop();

            PersistingMethods.Remove(Original);
            PersistingMethods.Remove(Replacement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CopyToStart(byte[] bytes, IntPtr methodStart) => Marshal.Copy(bytes, 0, methodStart, bytes.Length);
    }
}
