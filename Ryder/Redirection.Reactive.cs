using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;

namespace Ryder
{
    /// <summary>
    ///   Represents a dynamic <see cref="MethodRedirection"/> context.
    /// </summary>
    public sealed class RedirectionContext
    {
        private object returnValue;
        private object originalReturnValue;
        private bool hasComputedOriginalReturnValue;
        private bool isReturnValueSet;
        private readonly MethodRedirection methodRedirection;

        /// <summary>
        ///   Gets the object on which the <see cref="Method"/> was called.
        /// </summary>
        public object Sender { get; }

        /// <summary>
        ///   Gets a list of arguments passed to the original method.
        /// </summary>
        public ReadOnlyCollection<object> Arguments { get; }

        /// <summary>
        ///   Gets the <see cref="ReturnType"/> of the hooked method.
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        ///   Gets whether or not the <see cref="ReturnType"/> is a value type,
        ///   that thus cannot be <see langword="null"/>.
        /// </summary>
        public bool IsValueTypeReturned { get; }

        /// <summary>
        ///   Gets the method that called this hook.
        /// </summary>
        public MethodBase Method { get; }

        /// <summary>
        ///   Gets or sets the return value of the call.
        /// </summary>
        public object ReturnValue
        {
            get => returnValue;
            set
            {
                isReturnValueSet = true;

                if (value == null)
                {
                    if (IsValueTypeReturned)
                        returnValue = Activator.CreateInstance(ReturnType);

                    returnValue = null;
                    return;
                }

                if (value.GetType() != ReturnType)
                    throw new InvalidCastException($"Cannot convert {value.GetType()} to {ReturnType}.");

                returnValue = value;
            }
        }

        /// <summary>
        ///   Gets the return value that should have been returned.
        /// </summary>
        public object OriginalReturnValue
        {
            get
            {
                if (hasComputedOriginalReturnValue)
                    return originalReturnValue;

                object[] args = new object[Arguments.Count];
                Arguments.CopyTo(args, 0);
                
                hasComputedOriginalReturnValue = true;

                return originalReturnValue = Invoke(args);
            }
        }

        internal RedirectionContext(object sender, IList<object> args, MethodRedirection redirection)
        {
            Sender = sender;
            Method = redirection.Original;
            Arguments = new ReadOnlyCollection<object>(args);

            MethodBase original = redirection.Original;

            if (original is ConstructorInfo ctor)
                ReturnType = ctor.DeclaringType;
            else
                ReturnType = ((MethodInfo)original).ReturnType;

            bool isValueType = IsValueTypeReturned = ReturnType.GetTypeInfo().IsValueType;

            returnValue = isValueType ? Activator.CreateInstance(ReturnType) : null;
            methodRedirection = redirection;
        }

        /// <summary>
        ///   Invokes the original method with the given arguments, and returns its result.
        /// </summary>
        public object Invoke(params object[] args) => methodRedirection.InvokeOriginal(Sender, args);

        /// <summary>
        /// <para>
        ///   Returns:
        /// </para>
        /// <para>
        ///     - if the return value has been changed by the user, then this value;
        /// </para>
        /// <para>
        ///     - otherwise, the value originally returned.
        /// </para>
        /// </summary>
        internal object GetCustomReturnValueOrOriginal() => isReturnValueSet ? returnValue : OriginalReturnValue;
    }

    partial class Redirection
    {
        /// <summary>
        /// <para>
        ///   Dictionary that contains all active reactive <see cref="MethodRedirection"/>s.
        /// </para>
        /// <para>
        ///   This dictionary is used by the generated methods.
        /// </para>
        /// </summary>
        internal static readonly Dictionary<int, ReactiveRedirection> ObservingRedirections = new Dictionary<int, ReactiveRedirection>();

        /// <summary>
        ///   Returns an observable that allows observing a method, and hooking its calls.
        /// </summary>
        public static IObservable<RedirectionContext> Observe(MethodBase method)
        {
            MethodRedirection redirection = CreateDynamicRedirection(method, out int id);

            return new ReactiveRedirection(id, redirection);
        }

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
    }

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
        ///   Gets the key of this redirection in <see cref="Ryder.Redirection.ObservingRedirections"/>.
        /// </summary>
        public int Key { get; }

        internal ReactiveRedirection(int id, MethodRedirection redirection)
        {
            Redirection = redirection;
            Observers   = new List<IObserver<RedirectionContext>>();
            Key         = id;

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
}
