using System;
using System.Collections.Generic;

namespace Ryder
{
    /// <summary>
    ///   Represents an observable <see cref="Redirection"/>.
    /// </summary>
    public sealed class ObservableRedirection : Redirection, IObservable<RedirectionContext>
    {
        /// <summary>
        ///   Gets the underlying <see cref="MethodRedirection"/>.
        /// </summary>
        internal MethodRedirection UnderlyingRedirection { get; }

        /// <summary>
        ///   Gets a list of all observers of the underlying <see cref="MethodRedirection"/>.
        /// </summary>
        internal List<IObserver<RedirectionContext>> Observers { get; }

        /// <summary>
        ///   The key of this redirection in <see cref="Redirection.ObservingRedirections"/>.
        /// </summary>
        private readonly int Key;

        internal ObservableRedirection(int id, MethodRedirection redirection)
        {
            UnderlyingRedirection = redirection;
            Observers = new List<IObserver<RedirectionContext>>();
            Key = id;

            ObservingRedirections.Add(id, this);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<RedirectionContext> observer)
        {
            Observers.Add(observer);

            // Optionally start the redirection, in case it wasn't redirecting calls
            UnderlyingRedirection.Start();

            return new Disposable(this, observer);
        }

        /// <summary>
        ///   Subscribes to the <see cref="IObserver{T}.OnNext"/> event using the specified <paramref name="callback"/>,
        ///   and optionally reroute errors to the <paramref name="onError"/> <see cref="Action"/>.
        /// </summary>
        public IDisposable Subscribe(Action<RedirectionContext> callback, Action<Exception> onError = null)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return Subscribe(new RedirectionObserver(callback, onError));
        }

        /// <summary>
        ///   Unsubscribes the given <paramref name="observer"/>.
        /// </summary>
        private void Unsubscribe(IObserver<RedirectionContext> observer)
        {
            Observers.Remove(observer);

            if (Observers.Count == 0)
                // Stop the redirection, since noone is there to handle it.
                UnderlyingRedirection.Stop();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            UnderlyingRedirection.Dispose();
            ObservingRedirections.Remove(Key);
        }

        /// <inheritdoc />
        public override bool IsRedirecting
        {
            get => UnderlyingRedirection.IsRedirecting;
            set => UnderlyingRedirection.IsRedirecting = value;
        }

        /// <inheritdoc />
        public override void Start() => UnderlyingRedirection.Start();

        /// <inheritdoc />
        public override void Stop() => UnderlyingRedirection.Stop();

        /// <summary>
        ///   <see cref="IDisposable"/> returned to observers calling <see cref="Subscribe(System.IObserver{Ryder.RedirectionContext})"/>.
        /// </summary>
        private struct Disposable : IDisposable
        {
            private readonly ObservableRedirection Redirection;
            private readonly IObserver<RedirectionContext> Observer;

            internal Disposable(ObservableRedirection redirection, IObserver<RedirectionContext> observer)
            {
                Redirection = redirection;
                Observer = observer;
            }

            /// <inheritdoc />
            void IDisposable.Dispose() => Redirection.Unsubscribe(Observer);
        }
    }
}