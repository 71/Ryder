using System;
using System.ComponentModel;
using System.Reflection;

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
        public bool IsRedirecting
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

        private const string AbstractError = "Expected non-abstract method.";
        private const string SignatureError = "Expected same signature.";

        private static void CheckParameters(ParameterInfo a, ParameterInfo b, string paramName)
        {
            if (a.ParameterType != b.ParameterType)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same type.", paramName);
            if (a.IsOut != b.IsOut || a.IsIn != b.IsIn)
                throw new ArgumentException($"Expected parameters '{a}' and '{b}' to have the same signature.", paramName);
        }

        /// <summary>
        /// <para>
        ///   Gets or sets whether or not some checks should be disabled when creating
        ///   a <see cref="Redirection"/>.
        /// </para>
        /// <para>
        ///   Warning: Those checks are done for a reason, but may, in some cases, keep something
        ///   completely legal from happing.
        /// </para>
        /// </summary>
        /// <seealso href="https://github.com/6A/Ryder/blob/master/Ryder/Redirection.cs">
        ///   Code for this class, to see what checks are done, and what checks can be skipped.
        /// </seealso>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static bool SkipChecks { get; set; }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method or constructor
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        private static MethodRedirection RedirectCore(MethodBase original, MethodBase replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            // Check if abstract
            if (original.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));
            if (replacement.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Skip checks if needed
            if (SkipChecks)
                goto End;

            // Get return type
            Type originalReturnType = (original as MethodInfo)?.ReturnType ?? (original as ConstructorInfo)?.DeclaringType;

            if (originalReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(original));

            Type replacementReturnType = (replacement as MethodInfo)?.ReturnType ?? (replacement as ConstructorInfo)?.DeclaringType;

            if (replacementReturnType == null)
                throw new ArgumentException("Invalid method.", nameof(replacement));

            // Check return type
            if (originalReturnType != replacementReturnType)
                throw new ArgumentException("Expected both methods to have the same return type.", nameof(replacement));

            // Check signature
            ParameterInfo[] originalParams = original.GetParameters();
            ParameterInfo[] replacementParams = replacement.GetParameters();

            int length = originalParams.Length;
            int diff = 0;

            if (!original.IsStatic)
            {
                if (replacement.IsStatic)
                {
                    // Should have:
                    // instance i.original(a, b) | static replacement(i, a, b)

                    if (replacementParams.Length == 0 || replacementParams[0].ParameterType != original.DeclaringType)
                        throw new ArgumentException($"Expected first parameter of type '{original.DeclaringType}'.", nameof(replacement));
                    if (replacementParams.Length != originalParams.Length + 1)
                        throw new ArgumentException(SignatureError, nameof(replacement));

                    diff = -1;
                    // No need to set length, it's already good
                }
                else
                {
                    // Should have:
                    // instance i.original(a, b) | instance i.replacement(a, b)
                    
                    if (replacementParams.Length != originalParams.Length)
                        throw new ArgumentException(SignatureError, nameof(replacement));
                }
            }
            else if (!replacement.IsStatic)
            {
                // Should have:
                // static original(i, a, b) | instance i.replacement(a, b)

                if (originalParams.Length == 0 || originalParams[0].ParameterType != replacement.DeclaringType)
                    throw new ArgumentException($"Expected first parameter of type '{replacement.DeclaringType}'.", nameof(original));
                if (replacementParams.Length != originalParams.Length - 1)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                diff = 1;
                length--;
            }
            else
            {
                // Should have:
                // static original(a, b) | static replacement(a, b)

                if (originalParams.Length != replacementParams.Length)
                    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // At this point all parameters will have the same index with "+ diff",
            // and the parameters not checked in this loop have already been checked. We good.
            for (int i = diff == -1 ? 1 : 0; i < length; i++)
            {
                CheckParameters(originalParams[i + diff], replacementParams[i], nameof(replacement));
            }

            End:
            return new MethodRedirection(original, replacement, true);
        }

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> method
        ///   to the <paramref name="replacement"/> method.
        /// </summary>
        /// <param name="original">The <see cref="MethodBase"/> of the method whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="MethodBase"/> of the method providing the redirection.</param>
        public static MethodRedirection Redirect(MethodInfo original, MethodInfo replacement)
            => RedirectCore(original, replacement);

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> constructor
        ///   to the <paramref name="replacement"/> constructor.
        /// </summary>
        /// <param name="original">The <see cref="ConstructorInfo"/> of the constructor whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="ConstructorInfo"/> of the method providing the redirection.</param>
        public static MethodRedirection Redirect(ConstructorInfo original, MethodInfo replacement)
            => RedirectCore(original, replacement);

        /// <summary>
        ///   Redirects calls to the <paramref name="original"/> <see langword="delegate"/>
        ///   to the <paramref name="replacement"/> <see langword="delegate"/>.
        /// </summary>
        /// <param name="original">The <see cref="Delegate"/> whose calls shall be redirected.</param>
        /// <param name="replacement">The <see cref="Delegate"/> providing the redirection.</param>
        public static MethodRedirection Redirect<TDelegate>(TDelegate original, TDelegate replacement)
            where TDelegate : class
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            Delegate originalDel = original as Delegate;

            if (originalDel == null)
                throw new ArgumentException($"Expected a delegate, but got a {original.GetType()}.");

            Delegate replacementDel = replacement as Delegate;

            if (replacementDel == null)
                throw new ArgumentException($"Expected a delegate, but got a {replacement.GetType()}.");

            return Redirect(originalDel.GetMethodInfo(), replacementDel.GetMethodInfo());
        }


        /// <summary>
        ///   Redirects accesses to the <paramref name="original"/> property
        ///   to the <paramref name="replacement"/> property.
        /// </summary>
        /// <param name="original">The <see cref="PropertyInfo"/> of the property whose accesses shall be redirected.</param>
        /// <param name="replacement">The <see cref="PropertyInfo"/> of the property providing the redirection.</param>
        public static PropertyRedirection Redirect(PropertyInfo original, PropertyInfo replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            if (SkipChecks)
                goto End;

            // Check original
            MethodInfo anyOriginalMethod = original.GetMethod ?? original.SetMethod;

            if (anyOriginalMethod == null)
                throw new ArgumentException("The property must define a getter and/or a setter.", nameof(original));
            if (anyOriginalMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));

            // Check replacement
            MethodInfo anyReplacementMethod = replacement.GetMethod ?? replacement.SetMethod;

            if (anyReplacementMethod == null)
                throw new ArgumentException("The property must define a getter and/or a setter.", nameof(replacement));
            if (anyReplacementMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Check match: static
            if (!anyOriginalMethod.IsStatic)
            {
                if (anyReplacementMethod.IsStatic)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                // Check match: instance of same type
                // Actually, I ain't doing it just yet. There are cases where the declaring
                // type is different.
                //if (original.DeclaringType != replacement.DeclaringType)
                //    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // Check match: property type
            if (original.PropertyType != replacement.PropertyType)
                throw new ArgumentException("Expected same property type.", nameof(replacement));

            // Presence of corresponding get and set methods will be checked in the constructor.
            End:
            return new PropertyRedirection(original, replacement, true);
        }


        /// <summary>
        /// <para>
        ///   Redirects accesses to the <paramref name="original"/> event
        ///   to the <paramref name="replacement"/> event.
        /// </para>
        /// <para>
        ///   Please be aware that although the <see langword="add"/> and <see langword="remove"/>
        ///   methods are hooked, a simple redirection cannot redirect compiler-generated event raises.
        /// </para>
        /// <para>
        ///   If you truly want to redirect such calls, make sure you know how default events are compiled,
        ///   and replace their underlying field through reflection as you wish.
        /// </para>
        /// </summary>
        /// <param name="original">The <see cref="EventInfo"/> of the event whose accesses shall be redirected.</param>
        /// <param name="replacement">The <see cref="EventInfo"/> of the event providing the redirection.</param>
        public static EventRedirection Redirect(EventInfo original, EventInfo replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            if (SkipChecks)
                goto End;

            // Check original
            MethodInfo anyOriginalMethod = original.AddMethod ?? original.RemoveMethod ?? original.RaiseMethod;

            if (anyOriginalMethod == null)
                throw new ArgumentException("The event must define an add and/or remove and/or raise method.", nameof(original));
            if (anyOriginalMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(original));

            // Check replacement
            MethodInfo anyReplacementMethod = replacement.AddMethod ?? replacement.RemoveMethod ?? replacement.RaiseMethod;

            if (anyReplacementMethod == null)
                throw new ArgumentException("The event must define an add and/or remove and/or raise method.", nameof(replacement));
            if (anyReplacementMethod.IsAbstract)
                throw new ArgumentException(AbstractError, nameof(replacement));

            // Check match: static
            if (!anyOriginalMethod.IsStatic)
            {
                if (anyReplacementMethod.IsStatic)
                    throw new ArgumentException(SignatureError, nameof(replacement));

                // Check match: instance of same type
                // See property for why this is commented ^
                //if (original.DeclaringType != replacement.DeclaringType)
                //    throw new ArgumentException(SignatureError, nameof(replacement));
            }

            // Check match: event type
            if (original.EventHandlerType != replacement.EventHandlerType)
                throw new ArgumentException("Expected same event handler type.", nameof(replacement));

            // Presence of corresponding add, remove and raise methods will be checked in the constructor.
            End:
            return new EventRedirection(original, replacement, true);
        }


        #region LINQ Expressions
        // I'm planning to drop support for LINQ expressions, because they're not very useful,
        // but a big dependency nonetheless.
#if false
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
        #endregion
    }
}
