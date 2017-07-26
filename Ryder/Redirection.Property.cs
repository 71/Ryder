using System;
using System.Reflection;

namespace Ryder
{
    /// <summary>
    ///   Class that provides full control over a property <see cref="Redirection"/>.
    /// </summary>
    public sealed class PropertyRedirection : Redirection
    {
        /// <summary>
        ///   Gets the original <see cref="PropertyInfo"/>.
        /// </summary>
        public PropertyInfo Original { get; }

        /// <summary>
        ///   Gets the replacing <see cref="PropertyInfo"/>.
        /// </summary>
        public PropertyInfo Replacement { get; }

        /// <summary>
        ///   Gets the <see cref="MethodRedirection"/> providing redirection for the
        ///   <see cref="PropertyInfo.GetMethod"/>.
        /// </summary>
        public MethodRedirection GetRedirection { get; }

        /// <summary>
        ///   Gets the <see cref="MethodRedirection"/> providing redirection for the
        ///   <see cref="PropertyInfo.SetMethod"/>.
        /// </summary>
        public MethodRedirection SetRedirection { get; }

        internal PropertyRedirection(PropertyInfo original, PropertyInfo replacement, bool start = false)
        {
            Original = original;
            Replacement = replacement;

            if (original.GetMethod != null)
            {
                if (replacement.GetMethod == null)
                    throw new ArgumentException("A get method must be defined.", nameof(replacement));

                GetRedirection = new MethodRedirection(original.GetMethod, replacement.GetMethod, start);
            }

            if (original.SetMethod != null)
            {
                if (replacement.SetMethod == null)
                    throw new ArgumentException("A set method must be defined.", nameof(replacement));
                
                SetRedirection = new MethodRedirection(original.SetMethod, replacement.SetMethod, start);
            }
        }

        /// <summary>
        ///   Starts redirecting the <see langword="get"/> and <see langword="set"/> methods.
        /// </summary>
        public override void Start()
        {
            // Always start them, because the user might have changed
            // their state individually
            GetRedirection?.Start();
            SetRedirection?.Start();

            if (isRedirecting)
                return;

            isRedirecting = true;
        }

        /// <summary>
        ///   Starts redirecting the <see langword="get"/> and <see langword="set"/> methods.
        /// </summary>
        public override void Stop()
        {
            // Always stop them, because the user might have changed
            // their state individually
            GetRedirection?.Stop();
            SetRedirection?.Stop();

            if (!isRedirecting)
                return;

            isRedirecting = false;
        }

        /// <summary>
        ///   Calls <see cref="PropertyInfo.GetValue(object)"/> on the original
        ///   <see cref="PropertyInfo"/>.
        /// </summary>
        public object GetOriginal(object obj)
        {
            if (GetRedirection == null)
                throw new InvalidOperationException("A get method must be defined.");

            return GetRedirection.InvokeOriginal(obj);
        }

        /// <summary>
        ///   Calls <see cref="PropertyInfo.GetValue(object, object[])"/> on the original
        ///   <see cref="PropertyInfo"/>.
        /// </summary>
        public object GetOriginal(object obj, params object[] indices)
        {
            if (GetRedirection == null)
                throw new InvalidOperationException("A get method must be defined.");

            return GetRedirection.InvokeOriginal(obj, indices);
        }

        /// <summary>
        ///   Calls <see cref="PropertyInfo.SetValue(object, object)"/> on the original
        ///   <see cref="PropertyInfo"/>.
        /// </summary>
        public void SetOriginal(object obj, object value)
        {
            if (SetRedirection == null)
                throw new InvalidOperationException("A set method must be defined.");

            SetRedirection.InvokeOriginal(obj, value);
        }

        /// <summary>
        ///   Calls <see cref="PropertyInfo.SetValue(object, object, object[])"/> on the original
        ///   <see cref="PropertyInfo"/>.
        /// </summary>
        public void SetOriginal(object obj, object value, params object[] indices)
        {
            if (SetRedirection == null)
                throw new InvalidOperationException("A set method must be defined.");

            SetRedirection.InvokeOriginal(obj, value, indices);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            GetRedirection?.Dispose();
            SetRedirection?.Dispose();
        }
    }
}
