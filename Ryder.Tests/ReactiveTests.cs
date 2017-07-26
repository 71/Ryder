using System;
using System.Reflection;
using System.Reactive.Linq;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   <see cref="ReactiveRedirection"/> tests.
    /// </summary>
    public class ReactiveTests
    {
        /// <summary>
        ///   Ensures that <see cref="Redirection.Observe(MethodBase)"/> returns a working
        ///   <see cref="ReactiveRedirection"/>.
        /// </summary>
        [Fact]
        public void TestReactiveMethod()
        {
            MethodInfo method = typeof(DateTime)
                .GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public)
                .GetGetMethod();

            int count = 0;
            DateTime bday = new DateTime(1955, 10, 28);

            using (Redirection.Observe(method)
                              .Where(_ => count++ % 2 == 0)
                              .Subscribe(ctx => ctx.ReturnValue = bday))
            {
                Assert.Equal(DateTime.Now, bday);
                Assert.NotEqual(DateTime.Now, bday);
                Assert.Equal(DateTime.Now, bday);
                Assert.NotEqual(DateTime.Now, bday);
            }

            Assert.NotEqual(DateTime.Now, bday);
            Assert.NotEqual(DateTime.Now, bday);

            Assert.Equal(count, 4);
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

            Assert.NotEqual(DateTime.Now, birthday);

            using (Redirection.Observe(method, ctx => ctx.ReturnValue = birthday))
            {
                Assert.Equal(DateTime.Now, birthday);
            }

            Assert.NotEqual(DateTime.Now, birthday);
        }
    }
}
