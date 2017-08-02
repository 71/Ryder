using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

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
        /// <para>
        ///   Gets or sets the return value of the call.
        /// </para>
        /// <para>
        ///   If not set manually, then <see cref="OriginalReturnValue"/> will be returned,
        ///   unless the method returns <see langword="void"/>, in which case nothing is returned
        ///   (and the original method is not called).
        /// </para>
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

                if (!ReturnType.IsInstanceOfType(value))
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
        internal object GetCustomReturnValueOrOriginal()
        {
            if (ReturnType == typeof(void))
                return null;

            return isReturnValueSet ? returnValue : OriginalReturnValue;
        }
    }
}