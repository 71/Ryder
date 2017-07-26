using System;
using System.Reflection;
using System.Reactive.Linq;
using Xunit;

namespace Ryder.Tests
{
    public class ReactiveTests
    {
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
    }
}
