using System;
using System.Reflection;

namespace Ryder
{
    /// <summary>
    ///   Represents a <see cref="Redirection"/> observer.
    /// </summary>
    internal sealed class RedirectionObserver : IObserver<RedirectionContext>
    {
        /// <summary>
        ///   Action to which we'll delegate <see cref="OnNext(Ryder.RedirectionContext)"/>.
        /// </summary>
        private readonly Action<RedirectionContext> NextAction;

        /// <summary>
        ///   Action to which we'll delegate <see cref="OnError(System.Exception)"/>.
        /// </summary>
        private readonly Action<Exception> ErrorAction;

        internal RedirectionObserver(Action<RedirectionContext> next, Action<Exception> error)
        {
            NextAction = next;
            ErrorAction = error;
        }

        /// <inheritdoc />
        public void OnError(Exception error) => ErrorAction?.Invoke(error);

        /// <inheritdoc />
        public void OnNext(RedirectionContext value) => NextAction(value);

        /// <inheritdoc />
        public void OnCompleted()
        {
            // Never completes.
        }
    }
}