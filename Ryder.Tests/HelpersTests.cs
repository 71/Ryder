using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        [Fact(Skip = "Inlining behavior is not understood enough.")]
        public void TestInlinedMethods()
        {
            MethodInfo inlinedMethod = GetType().GetMethod(nameof(LikelyInlined), BindingFlags.Static | BindingFlags.Public);
            MethodInfo nonInlinedMethod = GetType().GetMethod(nameof(UnlikelyInlined), BindingFlags.Static | BindingFlags.Public);

            IntPtr inlinedStart = inlinedMethod.GetRuntimeMethodHandle().GetMethodStart();
            IntPtr nonInlinedStart = nonInlinedMethod.GetRuntimeMethodHandle().GetMethodStart();

            // IntPtr inlinedTarget = inlinedStart + Marshal.ReadInt32(inlinedStart + 1) + 5;
            // IntPtr nonInlinedTarget = nonInlinedStart + Marshal.ReadInt32(nonInlinedStart + 1) + 5;

            // byte[] inl = new byte[16];
            // byte[] noinl = new byte[16];

            // Marshal.Copy(inlinedTarget, inl, 0, 16);
            // Marshal.Copy(nonInlinedTarget, noinl, 0, 16);

            inlinedStart.HasBeenCompiled().ShouldBeFalse();
            nonInlinedStart.HasBeenCompiled().ShouldBeFalse();

            LikelyInlined().ShouldBe(UnlikelyInlined());

            // I tried testing inlining after some operations, but
            // the results all change depending on the configuration, and i don't wanna mess with this.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LikelyInlined() => 42;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int UnlikelyInlined() => 42;
    }
}
