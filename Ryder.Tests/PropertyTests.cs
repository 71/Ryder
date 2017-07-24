using System;
using Xunit;

namespace Ryder.Tests
{
    public class PropertyTests
    {
        public static DateTime Random => new DateTime().AddSeconds(new Random().NextDouble() * 2_000_000_000);

        [Fact]
        public void TestStaticProperties()
        {
            Assert.NotEqual(DateTime.Now, Random);

            using (Redirection.Redirect(() => Random, () => DateTime.Now))
            {
                Assert.Equal(DateTime.Now, Random);
            }

            Assert.NotEqual(DateTime.Now, Random);
        }

        public virtual int Value => 1;

        [Fact]
        public void TestInstanceProperties()
        {
            OverridePropertyTests overriden = new OverridePropertyTests();

            Assert.NotEqual(Value, overriden.Value);

            using (Redirection.Redirect(() => this.Value, () => overriden.Value))
            {
                Assert.Equal(Value, overriden.Value);
            }

            Assert.NotEqual(Value, overriden.Value);
        }

        private sealed class OverridePropertyTests : PropertyTests
        {
            public override int Value => 2;
        }
    }
}
