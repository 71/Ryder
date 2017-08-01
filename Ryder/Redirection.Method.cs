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
            RuntimeMethodHandle originalHandle = Helpers.GetRuntimeMethodHandle(original);
            RuntimeMethodHandle replacementHandle = Helpers.GetRuntimeMethodHandle(replacement);

            IntPtr originalStart = originalMethodStart = Helpers.GetMethodStart(originalHandle);
            IntPtr replacementStart = Helpers.GetMethodStart(replacementHandle);

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

            if (start)
            {
                CopyToStart(replBytes, originalStart);
                isRedirecting = true;
            }

#if false
            // TODO: Add support for .NET Standard 2.0 when it actually gets released in VS.
            RuntimeHelpers.PrepareMethod(originalHandle);
            RuntimeHelpers.PrepareMethod(replacementHandle);
#endif

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

    partial class Redirection
    {
        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method or constructor
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        private static MethodRedirection RedirectCore(MethodBase original, MethodBase replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check if abstract
            if (original.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));
            if (replacement.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Skip checks if needed
            if (SkipChecks)
                goto End;

            // Get return type
            Type originalReturnType = (original as MethodInfo)?.ReturnType ?? (original as ConstructorInfo)?.DeclaringType;

            if (originalReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(original));

            Type replacementReturnType = (replacement as MethodInfo)?.ReturnType ?? (replacement as ConstructorInfo)?.DeclaringType;

            if (replacementReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(replacement));

            // Check return type
            if (originalReturnType != replacementReturnType)
                throw new ArgumentException("Expected both methods to have the same return type.", nameof(replacement));

            // Check signature
            ParameterInfo[] originalParams = original.GetParameters();
            ParameterInfo[] replacementParams = replacement.GetParameters();

            int length = originalParams.Length;
            int diff = 0;

            if (!original.IsStatic)
            {
                if (replacement.IsStatic)
                {
                    // Should have:
                    // instance i.original(a, b) | static replacement(i, a, b)

                    if (replacementParams.Length == 0 || replacementParams[0].ParameterType != original.DeclaringType)
                        throw new ArgumentException($"Expected first parameter of type '{original.DeclaringType}'.", nameof(replacement));
                    if (replacementParams.Length != originalParams.Length + 1)
                        throw new ArgumentException(SignatureError, nameof(replacement));

                    diff = -1;
                    // No need to set length, it's already good
                }
                else
                {
                    // Should have:
                    // instance i.original(a, b) | instance i.replacement(a, b)
                    
                    if (replacementParams.Length != originalParams.Length)
                        throw new ArgumentException(SignatureError, nameof(replacement));
                }
            }
            else if (!replacement.IsStatic)
            {
                // Should have:
                // static original(i, a, b) | instance i.replacement(a, b)

                if (originalParams.Length == 0 || originalParams[0].ParameterType != replacement.DeclaringType)
                    throw new ArgumentException($"Expected first parameter of type '{replacement.DeclaringType}'.", nameof(original));
                if (replacementParams.Length != originalParams.Length - 1)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                diff = 1;
                length--;
            }
            else
            {
                // Should have:
                // static original(a, b) | static replacement(a, b)

                if (originalParams.Length != replacementParams.Length)
                    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // At this point all parameters will have the same index with "+ diff",
            // and the parameters not checked in this loop have already been checked. We good.
            for (int i = diff == -1 ? 1 : 0; i < length; i++)
            {
                CheckParameters(originalParams[i + diff], replacementParams[i], nameof(replacement));
            }

            End:
            return new MethodRedirection(original, replacement, true);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        public static MethodRedirection Redirect(MethodInfo original, MethodInfo replacement)
            => RedirectCore(original, replacement);

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> constructor
        ///   to the <paramref name="replacement"/> constructor.
        /// </summary>
        /// <param name="original">The <see cref="ConstructorInfo"/> of the constructor whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="ConstructorInfo"/> of the method providing the redirection.</param>
        public static MethodRedirection Redirect(ConstructorInfo original, MethodInfo replacement)
            => RedirectCore(original, replacement);

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> <see langword="delegate"/>
        ///   to the <paramref name="replacement"/> <see langword="delegate"/>.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        public static MethodRedirection Redirect<TDelegate>(TDelegate original, TDelegate replacement) where TDelegate : Delegate
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return Redirect(original.GetMethodInfo(), replacement.GetMethodInfo());
        }
    }
}
