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
                    throw new ArgumentException("", nameof(replacement));

                AddRedirection = new MethodRedirection(original.AddMethod, replacement.AddMethod, start);
            }

            if (original.RemoveMethod != null)
            {
                if (replacement.RemoveMethod == null)
                    throw new ArgumentException("", nameof(replacement));

                RemoveRedirection = new MethodRedirection(original.RemoveMethod, replacement.RemoveMethod, start);
            }

            if (original.RaiseMethod != null)
            {
                if (replacement.RaiseMethod == null)
                    throw new ArgumentException("", nameof(replacement));

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
}
