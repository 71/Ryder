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

        internal MethodRedirection(MethodBase original, MethodBase replacement, bool start)
        {
            Original = original;
            Replacement = replacement;

            // Note: I'm making local copies of the following fields to avoid accessing fields multiple times.
            RuntimeMethodHandle originalHandle = original.GetRuntimeMethodHandle();
            RuntimeMethodHandle replacementHandle = replacement.GetRuntimeMethodHandle();

            const string JIT_ERROR = "The specified method hasn't been jitted yet, and thus cannot be used in a redirection.";

            // Fetch their respective start
            IntPtr originalStart = originalHandle.GetMethodStart();
            IntPtr replacementStart = replacementHandle.GetMethodStart();

            // Edge case: calling this on the same method
            if (originalStart == replacementStart)
                throw new InvalidOperationException("Cannot redirect a method to itself.");

            // Edge case: methods are too close to one another
            int difference = (int)Math.Abs(originalStart.ToInt64() - replacementStart.ToInt64());
            int sizeOfPtr = Marshal.SizeOf<IntPtr>();

            if ((sizeOfPtr == sizeof(long) && difference < 13) || (sizeOfPtr == sizeof(int) && difference < 7))
                throw new InvalidOperationException("Unable to redirect methods whose bodies are too close to one another.");

            // Make sure they're jitted
            if (!originalStart.HasBeenCompiled())
            {
                if (!Helpers.TryPrepareMethod(original, originalHandle))
                    throw new ArgumentException(JIT_ERROR, nameof(original));

                originalStart = originalHandle.GetMethodStart();
            }

            if (!replacementStart.HasBeenCompiled())
            {
                if (!Helpers.TryPrepareMethod(replacement, replacementHandle))
                    throw new ArgumentException(JIT_ERROR, nameof(replacement));

                replacementStart = replacementHandle.GetMethodStart();
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
        /// <param name="skipChecks">If <see langword="true"/>, some safety checks will be omitted.</param>
        private static MethodRedirection RedirectCore(MethodBase original, MethodBase replacement, bool skipChecks)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check if replacement is abstract
            // We allow original abstract methods, though
            if (replacement.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Skip checks if needed
            if (skipChecks)
                goto End;

            // Get return type
            Type originalReturnType = (original as MethodInfo)?.ReturnType ?? (original as ConstructorInfo)?.DeclaringType;

            if (originalReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(original));

            Type replacementReturnType = (replacement as MethodInfo)?.ReturnType ?? (replacement as ConstructorInfo)?.DeclaringType;

            if (replacementReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(replacement));

            // Check return type
            if (!originalReturnType.IsAssignableFrom(replacementReturnType) &&
                !replacementReturnType.IsAssignableFrom(originalReturnType))
                throw new ArgumentException("Expected both methods to return compatible types.", nameof(replacement));

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

                    if (replacementParams.Length == 0)
                        throw new ArgumentException($"Expected first parameter of type '{original.DeclaringType}'.", nameof(replacement));
                    if (replacementParams.Length != originalParams.Length + 1)
                        throw new ArgumentException(SignatureError, nameof(replacement));

                    Type replThisType = replacementParams[0].ParameterType;
                    Type origThisType = original.DeclaringType;

                    if (!replThisType.IsAssignableFrom(origThisType) &&
                        !origThisType.IsAssignableFrom(replThisType))
                        throw new ArgumentException($"Expected first parameter assignable to or from '{origThisType}'.", nameof(replacement));

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

                if (originalParams.Length == 0)
                    throw new ArgumentException($"Expected first parameter of type '{replacement.DeclaringType}'.", nameof(original));
                if (replacementParams.Length != originalParams.Length - 1)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                Type replThisType = replacement.DeclaringType;
                Type origThisType = originalParams[0].ParameterType;

                if (!replThisType.IsAssignableFrom(origThisType) &&
                    !origThisType.IsAssignableFrom(replThisType))
                    throw new ArgumentException($"Expected first parameter assignable to or from '{origThisType}'.", nameof(replacement));

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

        #region Redirect
        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        /// <param name="skipChecks">If <see langword="true"/>, some safety checks will be omitted.</param>
        public static MethodRedirection Redirect(MethodInfo original, MethodInfo replacement, bool skipChecks = false)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return RedirectCore(original, replacement, skipChecks);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> constructor
        ///   to the <paramref name="replacement"/> constructor.
        /// </summary>
        /// <param name="original">The <see cref="ConstructorInfo"/> of the constructor whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="ConstructorInfo"/> of the method providing the redirection.</param>
        /// <param name="skipChecks">If <see langword="true"/>, some safety checks will be omitted.</param>
        public static MethodRedirection Redirect(ConstructorInfo original, MethodInfo replacement, bool skipChecks = false)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return RedirectCore(original, replacement, skipChecks);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> <see langword="delegate"/>
        ///   to the <paramref name="replacement"/> <see langword="delegate"/>.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        /// <param name="skipChecks">If <see langword="true"/>, some safety checks will be omitted.</param>
        public static MethodRedirection Redirect<TDelegate>(TDelegate original, TDelegate replacement, bool skipChecks = false)
            where TDelegate : Delegate
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return RedirectCore(original.GetMethodInfo(), replacement.GetMethodInfo(), skipChecks);
        }
        #endregion
    }
}
