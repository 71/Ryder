using System;
using System.Reflection;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;

// Required to avoid multiple redirections to edit the same method at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Ryder.Tests
{
    /// <summary>
    ///   <see cref="ObservableRedirection"/> tests.
    /// </summary>
    public class ReactiveTests
    {
        /// <summary>
        ///   Ensures that <see cref="Redirection.Observe(MethodBase)"/> returns a working
        ///   <see cref="ObservableRedirection"/>.
        /// </summary>
        [Fact]
        public void TestReactiveMethod()
        {
            MethodInfo method = typeof(DateTime)
                .GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public)
                .GetGetMethod();

            int count = 0;
            DateTime bday = new DateTime(1955, 10, 28);

            // Note: Observing this test through the debugger will call DateTime.Now,
            //       thus incrementing 'count', and breaking the test. Watch out.
            using (Redirection.Observe(method)
                              .Where(_ => count++ % 2 == 0)
                              .Subscribe(ctx => ctx.ReturnValue = bday))
            {
                DateTime.Now.ShouldBe(bday);
                DateTime.Now.ShouldNotBe(bday);
                DateTime.Now.ShouldBe(bday);
                DateTime.Now.ShouldNotBe(bday);
            }

            DateTime.Now.ShouldNotBe(bday);
            DateTime.Now.ShouldNotBe(bday);

            count.ShouldBe(4);
        }

        /// <summary>
        ///   Ensures that <see cref="Redirection.Observe(MethodBase, Action{RedirectionContext}, Action{Exception})"/>
        ///   returns a working <see cref="RedirectionObserver"/>.
        /// </summary>
        [Fact]
        public void TestBuiltInObserver()
        {
            DateTime birthday = new DateTime(1955, 10, 28);
            MethodInfo method = typeof(DateTime)
                .GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public)
                .GetGetMethod();

            DateTime.Now.ShouldNotBe(birthday);

            using (Redirection.Observe(method, ctx => ctx.ReturnValue = birthday))
            {
                DateTime.Now.ShouldBe(birthday);
            }

            DateTime.Now.ShouldNotBe(birthday);
        }

        /// <summary>
        ///   Ensures that instance methods are correctly redirected.
        /// </summary>
        [Fact]
        public void TestInstanceRedirection()
        {
            MethodInfo method = typeof(TestClass)
                .GetMethod(nameof(TestClass.ComputeHash), BindingFlags.Instance | BindingFlags.Public);

            const int SEED = 0xEA6C23;
            TestClass test = new TestClass(SEED);
            string testStr = "42";
            int testHash   = testStr.GetHashCode();

            test.ComputeHash(testStr).ShouldBe(unchecked(testHash * SEED));
            test.ComputeHash(testStr).ShouldNotBe(SEED);

            using (Redirection.Observe(method, ctx => ctx.ReturnValue = ((TestClass)ctx.Sender).Seed))
            {
                test.ComputeHash(testStr).ShouldBe(SEED);
            }

            test.ComputeHash(testStr).ShouldBe(unchecked(testHash * SEED));
        }

        private sealed class TestClass
        {
            public int Seed { get; }

            public TestClass(int seed) => Seed = seed;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int ComputeHash(object obj)
            {
                return unchecked(obj.GetHashCode() * Seed);
            }
        }
    }
}