using System;
using System.Reflection;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   <see cref="PropertyRedirection"/> tests.
    /// </summary>
    public class PropertyTests
    {
        /// <summary>
        ///   Returns a random <see cref="DateTime"/> between 1970-01-01 00:00:00
        ///   and 2000-01-01 00:00:00.
        /// </summary>
        public static DateTime Random => new DateTime(1970, 01, 01).AddSeconds(new Random().NextDouble() * 946_684_800);

        /// <summary>
        ///   Trims milliseconds from the given <see cref="DateTime"/> in order to
        ///   accept a margin of error up to a second in the following tests.
        /// </summary>
        public static DateTime Trim(DateTime dt) => dt.Millisecond == 0 ? dt : dt.AddMilliseconds(-dt.Millisecond);

        private static PropertyInfo RandomProperty
            => typeof(PropertyTests).GetProperty(nameof(Random), BindingFlags.Static | BindingFlags.Public);
        private static PropertyInfo NowProperty
            => typeof(DateTime).GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public);

        [Fact]
        public void TestStaticProperties()
        {
            Assert.NotEqual(Trim(DateTime.Now), Trim(Random));

            using (Redirection.Redirect(RandomProperty, NowProperty))
            {
                Assert.Equal(Trim(DateTime.Now), Trim(Random));
            }

            Assert.NotEqual(Trim(DateTime.Now), Trim(Random));
        }

        private static PropertyInfo BaseValueProperty
            => typeof(PropertyTests).GetProperty(nameof(Value), BindingFlags.Instance | BindingFlags.Public);
        private static PropertyInfo OverrideValueProperty
            => typeof(OverridePropertyTests).GetProperty(nameof(Value), BindingFlags.Instance | BindingFlags.Public);

        public virtual int Value => 1;

        [Fact]
        public void TestInstanceProperties()
        {
            OverridePropertyTests overriden = new OverridePropertyTests();

            Assert.NotEqual(Value, overriden.Value);

            using (Redirection.Redirect(BaseValueProperty, OverrideValueProperty))
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
