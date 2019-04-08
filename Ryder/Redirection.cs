using System;
using System.ComponentModel;
using System.Reflection;

#if NETCOREAPP2_0
using System.Linq.Expressions;
#endif

namespace Ryder
{
    /// <summary>
    ///   Defines a class that can redirect calls.
    /// </summary>
    public abstract partial class Redirection : IDisposable
    {
        internal bool isRedirecting;

        /// <summary>
        ///   Gets or sets whether or not the calls shall be redirected.
        /// </summary>
        public virtual bool IsRedirecting
        {
            get => isRedirecting;
            set
            {
                if (value == isRedirecting)
                    return;

                if (value)
                    Start();
                else
                    Stop();
            }
        }

        internal Redirection() { }

        /// <summary>
        ///   Starts redirecting calls.
        /// </summary>
        public abstract void Start();

        /// <summary>
        ///   Stops redirecting calls.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        ///   Disposes of the <see cref="Redirection"/>,
        ///   disabling it and removing static references made to
        ///   the needed objects.
        /// </summary>
        public abstract void Dispose();

        #region Static
        /// <summary>
        ///   Attempts to force JIT compilation of the given <paramref name="method"/>,
        ///   and returns a value describing whether or not it was successfully prepared.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool TryPrepare(MethodBase method)
        {
            return Helpers.TryPrepareMethod(method, method.GetRuntimeMethodHandle());
        }

        private const string AbstractError = "Expected non-abstract method.";
        private const string SignatureError = "Expected same signature.";

        private static void CheckParameters(ParameterInfo a, ParameterInfo b, string paramName)
        {
            if (a.ParameterType != b.ParameterType)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same type.", paramName);
            if (a.IsOut != b.IsOut || a.IsIn != b.IsIn)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same signature.", paramName);
        }

#if NETCOREAPP2_0
        // I'm planning to drop support for LINQ expressions, because they're not very useful,
        // but a big dependency nonetheless.
        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        public static MethodRedirection Redirect(Expression<Action> original, Expression<Action> replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            return Redirect(
                (original.Body as MethodCallExpression)?.Method ?? throw new ArgumentException("Invalid expression.", nameof(original)),
                (replacement.Body as MethodCallExpression)?.Method ?? throw new ArgumentException("Invalid expression.", nameof(replacement))
            );
        }

        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> member
        ///   to the <paramref name="replacement"/> member.
        /// </summary>
        /// <param name="original">
        ///   A <see cref="LambdaExpression"/> describing the member whose accesses shall be redirected.
        /// </param>
        /// <param name="replacement">
        ///   A <see cref="LambdaExpression"/> describing the member providing the redirection.
        /// </param>
        public static Redirection Redirect<T>(Expression<Func<T>> original, Expression<Func<T>> replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Make sure both expressions have the same type
            if (original.NodeType != replacement.NodeType)
                throw new ArgumentException($"Expected the '{original.NodeType}' NodeType.", nameof(replacement));

            // Maybe it's a method call?
            if (original.Body is MethodCallExpression originalCall &&
                replacement.Body is MethodCallExpression replacementCall)
            {
                return Redirect(originalCall.Method, replacementCall.Method);
            }

            // Then it has to be a member access.
            if (original.Body is MemberExpression originalMember &&
                     replacement.Body is MemberExpression replacementMember)
            {
                // Probably a property?
                if (originalMember.Member is PropertyInfo originalProp &&
                    replacementMember.Member is PropertyInfo replacementProp)
                {
                    return Redirect(originalProp, replacementProp);
                }

                // Then it has to be an event.
                if (originalMember.Member is EventInfo originalEvent &&
                    replacementMember.Member is EventInfo replacementEvent)
                {
                    return Redirect(originalEvent, replacementEvent);
                }

                // Not a property nor an event: it's an error
                throw new InvalidOperationException("The given member must be a property or an event.");
            }

            // Not a member access nor a call: it's an error.
            throw new InvalidOperationException($"The given expressions must be of type '{ExpressionType.MemberAccess}' or '{ExpressionType.Call}'.");
        }

        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> member
        ///   to the <paramref name="replacement"/> member.
        /// </summary>
        /// <param name="original">
        ///   A <see cref="LambdaExpression"/> describing the member whose accesses shall be redirected.
        /// </param>
        /// <param name="replacement">
        ///   A <see cref="LambdaExpression"/> describing the member providing the redirection.
        /// </param>
        public static TRedirection Redirect<T, TRedirection>(Expression<Func<T>> original, Expression<Func<T>> replacement)
            where TRedirection : Redirection => (TRedirection)Redirect(original, replacement);
#endif
        #endregion
    }
}
