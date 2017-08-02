using System;
using System.Reflection;

namespace Ryder
{
    /// <summary>
    ///   Class that provides full control over an event <see cref="Redirection"/>.
    /// </summary>
    public sealed class EventRedirection : Redirection
    {
        /// <summary>
        ///   Gets the original <see cref="EventInfo"/>.
        /// </summary>
        public EventInfo Original { get; }

        /// <summary>
        ///   Gets the replacing <see cref="EventInfo"/>.
        /// </summary>
        public EventInfo Replacement { get; }

        /// <summary>
        ///   Gets the <see cref="MethodRedirection"/> providing redirection for the
        ///   <see cref="EventInfo.AddMethod"/>.
        /// </summary>
        public MethodRedirection AddRedirection { get; }

        /// <summary>
        ///   Gets the <see cref="MethodRedirection"/> providing redirection for the
        ///   <see cref="EventInfo.RemoveMethod"/>.
        /// </summary>
        public MethodRedirection RemoveRedirection { get; }

        /// <summary>
        ///   Gets the <see cref="MethodRedirection"/> providing redirection for the
        ///   <see cref="EventInfo.RaiseMethod"/>.
        /// </summary>
        public MethodRedirection RaiseRedirection { get; }

        internal EventRedirection(EventInfo original, EventInfo replacement, bool start = false)
        {
            Original = original;
            Replacement = replacement;

            if (original.AddMethod != null)
            {
                if (replacement.AddMethod == null)
                    throw new ArgumentException("An add method must be defined.", nameof(replacement));

                AddRedirection = new MethodRedirection(original.AddMethod, replacement.AddMethod, start);
            }

            if (original.RemoveMethod != null)
            {
                if (replacement.RemoveMethod == null)
                    throw new ArgumentException("A remove method must be defined.", nameof(replacement));

                RemoveRedirection = new MethodRedirection(original.RemoveMethod, replacement.RemoveMethod, start);
            }

            if (original.RaiseMethod != null)
            {
                if (replacement.RaiseMethod == null)
                    throw new ArgumentException("A raise method must be defined.", nameof(replacement));

                RaiseRedirection = new MethodRedirection(original.RaiseMethod, replacement.RaiseMethod, start);
            }
        }

        /// <summary>
        ///   Starts redirecting the <see langword="add"/>, <see langword="remove"/>, and
        ///   <see langword="raise"/> methods.
        /// </summary>
        public override void Start()
        {
            // Always stop them, because the user might have changed
            // their state individually
            AddRedirection?.Start();
            RemoveRedirection?.Start();
            RaiseRedirection?.Start();

            if (isRedirecting)
                return;

            isRedirecting = true;
        }

        /// <summary>
        ///   Stops redirecting the <see langword="add"/>, <see langword="remove"/>, and
        ///   <see langword="raise"/> methods. 
        /// </summary>
        public override void Stop()
        {
            // Always stop them, because the user might have changed
            // their state individually
            AddRedirection?.Stop();
            RemoveRedirection?.Stop();
            RaiseRedirection?.Stop();

            if (!isRedirecting)
                return;

            isRedirecting = false;
        }

        /// <summary>
        ///   Calls <see cref="EventInfo.AddEventHandler(object, Delegate)"/>
        ///   on the original <see cref="EventInfo"/>.
        /// </summary>
        public void AddEventHandlerOriginal(object obj, Delegate handler)
        {
            if (AddRedirection == null)
                throw new InvalidOperationException();

            AddRedirection.InvokeOriginal(obj, handler);
        }

        /// <summary>
        ///   Calls <see cref="EventInfo.RemoveEventHandler(object, Delegate)"/>
        ///   on the original <see cref="EventInfo"/>.
        /// </summary>
        public void RemoveEventHandlerOriginal(object obj, Delegate handler)
        {
            if (RemoveRedirection == null)
                throw new InvalidOperationException();

            RemoveRedirection.InvokeOriginal(obj, handler);
        }

        /// <summary>
        ///   Raises the original <see cref="EventInfo"/>.
        /// </summary>
        public void RaiseOriginal(object obj, params object[] args)
        {
            if (RaiseRedirection == null)
                throw new InvalidOperationException();

            RaiseRedirection.InvokeOriginal(obj, args);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            AddRedirection?.Dispose();
            RemoveRedirection?.Dispose();
            RaiseRedirection?.Dispose();
        }
    }

    partial class Redirection
    {
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
        /// <param name="skipChecks">If <see langword="true"/>, some safety checks will be omitted.</param>
        public static EventRedirection Redirect(EventInfo original, EventInfo replacement, bool skipChecks = false)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));

            if (skipChecks)
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
    }
}
