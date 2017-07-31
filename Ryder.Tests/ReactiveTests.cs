using System;
using System.Reflection;
using System.Reactive.Linq;
using Shouldly;
using Xunit;

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
    }
}
