using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Ryder
{
    partial class Redirection
    {
        #region Public methods
        /// <summary>
        ///   Returns an observable that allows observing the specified <paramref name="method"/>,
        ///   and hooking its calls, optionally modifying its return type.
        /// </summary>
        public static IObservable<RedirectionContext> Observe(MethodBase method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            MethodRedirection redirection = CreateDynamicRedirection(method, out int id);

            return new ReactiveRedirection(id, redirection);
        }

        /// <summary>
        ///   Observes the specified <paramref name="method"/>, hooking its calls and optionally
        ///   modifying its return type.
        /// </summary>
        /// <returns>An <see cref="IDisposable"/> that can be disposed to disable the hook.</returns>
        public static IDisposable Observe(MethodBase method, Action<RedirectionContext> action, Action<Exception> onError = null)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return Observe(method).Subscribe(new RedirectionObserver(action, onError));
        }
        #endregion

        /// <summary>
        /// <para>
        ///   Dictionary that contains all active reactive <see cref="MethodRedirection"/>s.
        /// </para>
        /// <para>
        ///   This dictionary is used by the generated methods.
        /// </para>
        /// </summary>
        internal static readonly Dictionary<int, ReactiveRedirection> ObservingRedirections = new Dictionary<int, ReactiveRedirection>();

        #region Private utils
        /// <summary>
        ///   <see cref="Random"/> that generates IDs for <see cref="ObservingRedirections"/>.
        /// </summary>
        private static readonly Random ObservingRedirectionsIdGenerator = new Random();

        /// <summary>
        ///   Method invoked by hooked methods when they are themselves invoked.
        /// </summary>
        // ReSharper disable once SuggestBaseTypeForParameter
        private static object OnInvoked(object sender, object[] arguments, int key)
        {
            var reactiveRedirection = ObservingRedirections[key];
            RedirectionContext value = new RedirectionContext(sender, arguments, reactiveRedirection.Redirection);

            foreach (var observer in reactiveRedirection.Observers)
            {
                observer.OnNext(value);
            }

            return value.GetCustomReturnValueOrOriginal();
        }

        /// <summary>
        ///   Creates a <see cref="Redirection"/>
        /// </summary>
        private static MethodRedirection CreateDynamicRedirection(MethodBase method, out int id)
        {
            // Make id
            do
            {
                id = ObservingRedirectionsIdGenerator.Next();
            }
            while (ObservingRedirections.ContainsKey(id));

            // Creates an array containing all parameter types
            ParameterInfo[] originalParameters = method.GetParameters();
            Type[] originalParameterTypes = new Type[originalParameters.Length];

            for (int i = 0; i < originalParameters.Length; i++)
            {
                originalParameterTypes[i] = originalParameters[i].ParameterType;
            }

            // Create an identical method
            bool isCtor = method is ConstructorInfo;
            Type returnType = isCtor ? method.DeclaringType : ((MethodInfo)method).ReturnType;

            MethodAttributes attrs = MethodAttributes.Public;

            if (method.IsStatic)
                attrs |= MethodAttributes.Static;

            DynamicMethod dyn = new DynamicMethod(
                method.Name, attrs, CallingConventions.Standard,
                returnType, originalParameterTypes, method.DeclaringType, true);

            // Make the method call the observable
            ILGenerator il = dyn.GetILGenerator();
            {
                // This is in a block to make every more readable,
                // the following comments describe what's happening in the generated method.

                // Emit "this", or "null"
                il.Emit(method.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);

                // Create an array containing all parameters
                il.Emit(OpCodes.Ldc_I4, originalParameters.Length);
                il.Emit(OpCodes.Newarr, typeof(object));

                int diff = method.IsStatic ? 0 : 1;

                for (int i = 0; i < originalParameters.Length; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldloc, i + diff);

                    Type parameterType = originalParameterTypes[i];

                    if (parameterType.GetTypeInfo().IsValueType)
                        il.Emit(OpCodes.Box, parameterType);

                    il.Emit(OpCodes.Stelem_Ref);
                }

                // Array is still on stack (thanks to dup)
                // Emit id
                il.Emit(OpCodes.Ldc_I4, id);

                // Call "hook" method
                il.Emit(OpCodes.Call, typeof(Redirection).GetMethod(nameof(OnInvoked), BindingFlags.Static | BindingFlags.NonPublic));

                // Return returned result
                // (But first, cast it if needed)
                if (isCtor || returnType == typeof(void))
                    il.Emit(OpCodes.Pop);
                else if (returnType.GetTypeInfo().IsValueType)
                    il.Emit(OpCodes.Unbox_Any, returnType);
                else if (returnType != typeof(object))
                    il.Emit(OpCodes.Castclass, returnType);

                il.Emit(OpCodes.Ret);
            }

            // Return the redirection
            return new MethodRedirection(method, dyn, false);
        }
        #endregion
    }

    #region IObservable and IObserver implementations
    /// <summary>
    ///   Represents an observable <see cref="Ryder.Redirection"/>.
    /// </summary>
    internal sealed class ReactiveRedirection : IObservable<RedirectionContext>, IDisposable
    {
        /// <summary>
        ///   Gets the observed <see cref="MethodRedirection"/>.
        /// </summary>
        public MethodRedirection Redirection { get; }

        /// <summary>
        ///   Gets a list of all observers of the underlying <see cref="MethodRedirection"/>.
        /// </summary>
        public List<IObserver<RedirectionContext>> Observers { get; }

        /// <summary>
        ///   The key of this redirection in <see cref="Ryder.Redirection.ObservingRedirections"/>.
        /// </summary>
        private readonly int Key;

        internal ReactiveRedirection(int id, MethodRedirection redirection)
        {
            Redirection = redirection;
            Observers = new List<IObserver<RedirectionContext>>();
            Key = id;

            Ryder.Redirection.ObservingRedirections.Add(id, this);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<RedirectionContext> observer)
        {
            Observers.Add(observer);

            // Optionally start the redirection, in case it wasn't redirecting calls
            Redirection.Start();

            return new Disposable(this, observer);
        }

        /// <summary>
        ///   Unsubscribes the given <paramref name="observer"/>.
        /// </summary>
        private void Unsubscribe(IObserver<RedirectionContext> observer)
        {
            Observers.Remove(observer);

            if (Observers.Count == 0)
                // Stop the redirection, since noone is there to handle it.
                Redirection.Stop();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Redirection.Dispose();
            Ryder.Redirection.ObservingRedirections.Remove(Key);
        }

        /// <summary>
        ///   <see cref="IDisposable"/> returned to observers calling <see cref="Subscribe"/>.
        /// </summary>
        private struct Disposable : IDisposable
        {
            private readonly ReactiveRedirection Redirection;
            private readonly IObserver<RedirectionContext> Observer;

            internal Disposable(ReactiveRedirection redirection, IObserver<RedirectionContext> observer)
            {
                Redirection = redirection;
                Observer = observer;
            }

            /// <inheritdoc />
            void IDisposable.Dispose() => Redirection.Unsubscribe(Observer);
        }
    }

    /// <summary>
    ///   Represents a <see cref="Redirection"/> observer.
    /// </summary>
    internal sealed class RedirectionObserver : IObserver<RedirectionContext>
    {
        /// <summary>
        ///   Action to which we'll delegate <see cref="OnNext"/>.
        /// </summary>
        private readonly Action<RedirectionContext> NextAction;

        /// <summary>
        ///   Action to which we'll delegate <see cref="OnError"/>.
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
    #endregion
}
