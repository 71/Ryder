using Shouldly;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   Provides tests related to Ryder helpers.
    /// </summary>
    public class HelpersTests
    {
        /// <summary>
        ///   Try to see if the fixup corresponds.
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
    }
}
