using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   <see cref="MethodRedirection"/> tests.
    /// </summary>
    public class MethodTests
    {
        #region Static method with no parameters
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Original() => true;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Replacement() => false;

        [Fact]
        public void TestStaticMethods()
        {
            Assert.NotEqual(Original(), Replacement());

            using (Redirection.Redirect<Func<bool>>(Original, Replacement))
            {
                Assert.Equal(Original(), Replacement());
            }

            Assert.NotEqual(Original(), Replacement());
        }
        #endregion

        #region Static method with parameters
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int PlusOne(int number) => number + 1;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int PlusTwo(int number) => number + 2;

        [Fact]
        public void TestStaticMethodsWithParameters()
        {
            Assert.NotEqual(PlusOne(1), PlusTwo(1));

            using (Redirection.Redirect<Func<int, int>>(PlusOne, PlusTwo))
            {
                Assert.Equal(PlusOne(1), PlusTwo(1));
            }

            Assert.NotEqual(PlusOne(1), PlusTwo(1));
        }
        #endregion

        public int Value { get; set; }

        #region Instance method with no parameters
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void IncrementValue() => Value++;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DecrementValue() => Value--;

        [Fact]
        public void TestInstanceMethods()
        {
            MethodTests tests1 = new MethodTests { Value = 10 };
            MethodTests tests2 = new MethodTests { Value = 10 };

            Assert.Equal(tests1.Value, tests2.Value);

            tests1.IncrementValue();

            using (Redirection redirection = Redirection.Redirect<Action>(IncrementValue, DecrementValue))
            {
                tests2.IncrementValue();

                Assert.NotEqual(tests1.Value, tests2.Value);
                Assert.Equal(tests1.Value, 11);
                Assert.Equal(tests2.Value, 9);

                redirection.Stop();

                tests1.Value = tests2.Value = 0;

                tests1.IncrementValue();
                tests2.IncrementValue();

                Assert.Equal(tests1.Value, tests2.Value);
                Assert.Equal(tests1.Value, 1);
                Assert.Equal(tests2.Value, 1);
            }
        }
        #endregion

        #region Instance method with parameters
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void IncrementValueBy(int number) => Value += number;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DecrementValueBy(int number) => Value -= number;

        [Fact]
        public void TestInstanceMethodsWithParameters()
        {
            MethodTests tests1 = new MethodTests { Value = 10 };
            MethodTests tests2 = new MethodTests { Value = 10 };

            Assert.Equal(tests1.Value, tests2.Value);

            tests1.IncrementValueBy(5);

            using (Redirection redirection = Redirection.Redirect<Action<int>>(IncrementValueBy, DecrementValueBy))
            {
                tests2.IncrementValueBy(5);

                Assert.NotEqual(tests1.Value, tests2.Value);
                Assert.Equal(tests1.Value, 15);
                Assert.Equal(tests2.Value, 5);

                redirection.Stop();

                tests1.Value = tests2.Value = 0;

                tests1.IncrementValueBy(3);
                tests2.IncrementValueBy(3);

                Assert.Equal(tests1.Value, tests2.Value);
                Assert.Equal(tests1.Value, 3);
                Assert.Equal(tests2.Value, 3);
            }
        }
        #endregion

        #region InvokeOriginal
        [Fact]
        public void TestInvokeOriginal()
        {
            Assert.NotEqual(Original(), Replacement());

            using (var redirection = Redirection.Redirect<Func<bool>>(Original, Replacement))
            {
                Assert.Equal(Original(), Replacement());
                Assert.NotEqual(Original(), redirection.InvokeOriginal(null));

                redirection.Stop();

                Assert.Equal(Original(), redirection.InvokeOriginal(null));
            }
        }
        #endregion
    }
}
