using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Shouldly;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   Provides tests related to Ryder helpers.
    /// </summary>
    public class HelpersTests
    {
        /// <summary>
        ///   Tests Ryder's ability to determine whether or not a method is uninitialized.
        /// </summary>
        [Fact]
        public void TestUninitializedMethods()
        {
            bool IsValidMethod(MethodInfo method)
            {
                if (method.IsAbstract)
                    return false;

                if (!method.IsStatic && method.DeclaringType.GetTypeInfo().IsAbstract)
                    return false;

                return !method.ContainsGenericParameters;
            }

            // We're testing LINQ expressions here, cuz there are (instance / static) and (public / non-public) methods,
            // properties, and most methods are independant. Last time I checked, running this step checks
            // 193 different methods.
            foreach (var method in typeof(Expression).GetMethods(BindingFlags.Instance |
                                                                 BindingFlags.Static   |
                                                                 BindingFlags.Public   |
                                                                 BindingFlags.NonPublic)
                                                     .Where(IsValidMethod)
                                                     .GroupBy(x => x.Name)
                                                     .Select(Enumerable.First))
            {
                // Find non-jitted start
                IntPtr start = method.GetRuntimeMethodHandle().GetMethodStart();

                // Compile method (should work on this platform)
                Helpers.TryPrepareMethod(method, method.GetRuntimeMethodHandle()).ShouldBeTrue();

                // Find freshly jitted start
                IntPtr newStart = method.GetRuntimeMethodHandle().GetMethodStart();

                // start != newStart => it wasn't jitted before: Fixup should be good
                start.HasBeenCompiled().ShouldBe(start == newStart);

                // In any case, the new method shouldn't be a fixup
                newStart.HasBeenCompiled().ShouldBeTrue();
            }
        }

        /// <summary>
        ///   Tests Ryder's ability to determine whether or not a method is inlined.
        /// </summary>
        [Fact]
        public void TestInlinedMethods()
        {
            MethodInfo inlinedMethod = GetType().GetMethod(nameof(LikelyInlined), BindingFlags.Static | BindingFlags.Public);
            MethodInfo nonInlinedMethod = GetType().GetMethod(nameof(UnlikelyInlined), BindingFlags.Static | BindingFlags.Public);

            IntPtr inlinedStart = inlinedMethod.GetRuntimeMethodHandle().GetMethodStart();
            IntPtr nonInlinedStart = nonInlinedMethod.GetRuntimeMethodHandle().GetMethodStart();

            inlinedStart.HasBeenCompiled().ShouldBeFalse();
            nonInlinedStart.HasBeenCompiled().ShouldBeFalse();

            LikelyInlined().ShouldBe(UnlikelyInlined());

            inlinedStart = inlinedMethod.GetRuntimeMethodHandle().GetMethodStart();
            nonInlinedStart = nonInlinedMethod.GetRuntimeMethodHandle().GetMethodStart();

            inlinedStart.HasBeenCompiled().ShouldBeTrue();
            nonInlinedStart.HasBeenCompiled().ShouldBeTrue();

            byte[] inlinedBytes = new byte[12];
            byte[] nonInlinedBytes = new byte[12];

            Marshal.Copy(inlinedStart, inlinedBytes, 0, 12);
            Marshal.Copy(nonInlinedStart, nonInlinedBytes, 0, 12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LikelyInlined() => 42;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int UnlikelyInlined() => 42;
    }
}
