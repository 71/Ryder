using System;
using System.Runtime.CompilerServices;
using Shouldly;
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
            Original().ShouldNotBe(Replacement());

            using (Redirection.Redirect<Func<bool>>(Original, Replacement))
            {
                Original().ShouldBe(Replacement());
            }

            Original().ShouldNotBe(Replacement());
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
            PlusOne(1).ShouldNotBe(PlusTwo(1));

            using (Redirection.Redirect<Func<int, int>>(PlusOne, PlusTwo))
            {
                PlusOne(1).ShouldBe(PlusTwo(1));
            }

            PlusOne(1).ShouldNotBe(PlusTwo(1));
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

            tests1.Value.ShouldBe(tests2.Value);

            tests1.IncrementValue();

            using (Redirection redirection = Redirection.Redirect<Action>(IncrementValue, DecrementValue))
            {
                tests2.IncrementValue();

                tests1.Value.ShouldNotBe(tests2.Value);
                tests1.Value.ShouldBe(11);
                tests2.Value.ShouldBe(9);

                redirection.Stop();

                tests1.Value = tests2.Value = 0;

                tests1.IncrementValue();
                tests2.IncrementValue();

                tests1.Value.ShouldBe(tests2.Value);
                tests1.Value.ShouldBe(1);
                tests2.Value.ShouldBe(1);
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

            tests1.Value.ShouldBe(tests2.Value);

            tests1.IncrementValueBy(5);

            using (Redirection redirection = Redirection.Redirect<Action<int>>(IncrementValueBy, DecrementValueBy))
            {
                tests2.IncrementValueBy(5);

                tests1.Value.ShouldNotBe(tests2.Value);
                tests1.Value.ShouldBe(15);
                tests2.Value.ShouldBe(5);

                redirection.Stop();

                tests1.Value = tests2.Value = 0;

                tests1.IncrementValueBy(3);
                tests2.IncrementValueBy(3);

                tests1.Value.ShouldBe(tests2.Value);
                tests1.Value.ShouldBe(3);
                tests2.Value.ShouldBe(3);
            }
        }
        #endregion

        #region InvokeOriginal
        [Fact]
        public void TestInvokeOriginal()
        {
            Original().ShouldNotBe(Replacement());

            using (var redirection = Redirection.Redirect<Func<bool>>(Original, Replacement))
            {
                Original().ShouldBe(Replacement());
                Original().ShouldNotBe(redirection.InvokeOriginal(null));

                redirection.Stop();

                Original().ShouldBe(redirection.InvokeOriginal(null));
            }
        }
        #endregion

        #region Constructors
        [Fact]
        public void TestConstructors()
        {
            // TODO
        }
        #endregion
    }
}
