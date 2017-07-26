using System;
using System.Reflection;
using Shouldly;
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

        [Fact]
        public void TestStaticProperties()
        {
            PropertyInfo randomProperty = typeof(PropertyTests)
                .GetProperty(nameof(Random), BindingFlags.Static | BindingFlags.Public);
            PropertyInfo nowProperty = typeof(DateTime)
                .GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public);


            DateTime.Now.ShouldNotBe(Random, tolerance: TimeSpan.FromMilliseconds(100));

            using (Redirection.Redirect(randomProperty, nowProperty))
            {
                DateTime.Now.ShouldBe(Random, tolerance: TimeSpan.FromMilliseconds(100));
            }

            DateTime.Now.ShouldNotBe(Random, tolerance: TimeSpan.FromMilliseconds(100));
        }

        public virtual int Value => 1;

        [Fact]
        public void TestInstanceProperties()
        {
            PropertyInfo baseValueProperty = typeof(PropertyTests)
                .GetProperty(nameof(Value), BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo overrideValueProperty = typeof(OverridePropertyTests)
                .GetProperty(nameof(Value), BindingFlags.Instance | BindingFlags.Public);


            OverridePropertyTests overriden = new OverridePropertyTests();

            Value.ShouldNotBe(overriden.Value);

            using (Redirection.Redirect(baseValueProperty, overrideValueProperty))
            {
                Value.ShouldBe(overriden.Value);
            }

            Value.ShouldNotBe(overriden.Value);
        }

        private sealed class OverridePropertyTests : PropertyTests
        {
            public override int Value => 2;
        }
    }
}
