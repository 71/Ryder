using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Ryder.Tests
{
    /// <summary>
    ///   <see cref="EventRedirection"/> tests.
    /// </summary>
    public class EventTests
    {
        /// <summary>
        ///   Event invoked to multiply two values.
        /// </summary>
        public static event Func<int, int, int> OnMultiply;

        /// <summary>
        ///   Event invoked to add two values.
        /// </summary>
        public static event Func<int, int, int> OnAdd;

        private static int Multiply(int a, int b) => a * b;
        private static int Add(int a, int b) => a + b;

        [Fact]
        [SuppressMessage("ReSharper", "PossibleNullReferenceException", Justification = "Events aren't null, duh.")]
        public void TestStaticEvents()
        {
            // Note: Calling "OnMultiply" will still call "Multiply".
            //       In order to completely override this behavior, you'd have
            //       to replace the underlying delegate. You can simply do this by finding
            //       the compiler-generated "OnMultiply" field, and setting its value to the
            //       compiler-generated "OnAdd" field.
            EventInfo multiplyEvent = typeof(EventTests).GetEvent(nameof(OnMultiply), BindingFlags.Static | BindingFlags.Public);
            EventInfo addEvent      = typeof(EventTests).GetEvent(nameof(OnAdd), BindingFlags.Static | BindingFlags.Public);

            OnMultiply += Multiply;
            OnAdd += Add;

            using (Redirection.Redirect(multiplyEvent, addEvent))
            {
                OnMultiply.GetInvocationList().Length.ShouldBe(1);
                OnMultiply -= Multiply;
                OnMultiply.GetInvocationList().Length.ShouldBe(1);
            }

            OnMultiply.GetInvocationList().Length.ShouldBe(1);
            OnMultiply -= Multiply;
            OnMultiply.GetInvocationList().Length.ShouldBe(0);
        }
    }
}
