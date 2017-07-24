using System;
using System.Reflection;
using Xunit;

namespace Ryder.Tests
{
    public class PropertyTests
    {
        public static DateTime Random => new DateTime().AddSeconds(new Random().NextDouble() * 2_000_000_000);

        /// <summary>
        ///   Trims milliseconds from the given <see cref="DateTime"/>.
        /// </summary>
        private static DateTime Trim(DateTime dt)
        {
            return dt.Millisecond == 0 ? dt : dt.AddMilliseconds(-dt.Millisecond);
        }

        [Fact]
        public void TestStaticProperties()
        {
            Assert.NotEqual(Trim(DateTime.Now), Trim(Random));

            using (Redirection.Redirect(() => Random, () => DateTime.Now))
            {
                Assert.Equal(Trim(DateTime.Now), Trim(Random));
            }

            Assert.NotEqual(Trim(DateTime.Now), Trim(Random));
        }

        public virtual int Value => 1;

        [Fact]
        public void TestInstanceProperties()
        {
            OverridePropertyTests overriden = new OverridePropertyTests();

            Assert.NotEqual(Value, overriden.Value);

            using (Redirection.Redirect(typeof(PropertyTests).GetProperty(nameof(Value)), typeof(OverridePropertyTests).GetProperty(nameof(Value))))
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
