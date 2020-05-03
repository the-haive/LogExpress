using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace LogExpress.Utils
{
    /// <summary>Represents an Observable and thread-safe collection of key/value pairs that can be accessed by multiple threads concurrently.</summary>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    public class ObservableConcurrentDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        /// <summary>
        /// Locking is used for one of the GetOrAdd
        /// </summary>
        private readonly object _padLock = new object();

        /// <summary>Gets or sets the value associated with the specified key.</summary>
        /// <param name="key">The key of the value to get or set.</param>
        /// <returns>The value of the key/value pair at the specified index.</returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is  <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.Collections.Generic.KeyNotFoundException">
        ///     The property is retrieved and
        ///     <paramref name="key" /> does not exist in the collection.
        /// </exception>
        public new TValue this[TKey key]
        {
            get => base[key];
            set => AddOrUpdate(key, value, (k, v) => value);
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        ///     Uses the specified functions to add a key/value pair to the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> if the key does not already exist, or to
        ///     update a key/value pair in the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> if the key
        ///     already exists.
        /// </summary>
        /// <param name="key">The key to be added or whose value should be updated</param>
        /// <param name="addValueFactory">The function used to generate a value for an absent key</param>
        /// <param name="updateValueFactory">
        ///     The function used to generate a new value for an existing key based on the key's
        ///     existing value
        /// </param>
        /// <returns>
        ///     The new value for the key. This will be either be the result of <paramref name="addValueFactory" /> (if the
        ///     key was absent) or the result of <paramref name="updateValueFactory" /> (if the key was present).
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" />, <paramref name="addValueFactory" />, or <paramref name="updateValueFactory" /> is
        ///     <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.OverflowException">
        ///     The dictionary already contains the maximum number of elements (
        ///     <see cref="F:System.Int32.MaxValue" />).
        /// </exception>
        public new TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory,
            Func<TKey, TValue, TValue> updateValueFactory)
        {
            var wasUpdate = false;
            var value = base.AddOrUpdate(key, addValueFactory, (k, v) =>
            {
                wasUpdate = true;
                return updateValueFactory(k, v);
            });

            //TODO: Fix update-notifications -if possible. The following line does not work (NotifyCollectionChangedAction.Replace not allowed)
            //OnCollectionChanged(new NotifyCollectionChangedEventArgs(wasUpdate ? NotifyCollectionChangedAction.Replace : NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return value;
        }

        /// <summary>
        ///     Adds a key/value pair to the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />
        ///     if the key does not already exist, or updates a key/value pair in the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> by using the specified function if the key
        ///     already exists.
        /// </summary>
        /// <param name="key">The key to be added or whose value should be updated</param>
        /// <param name="addValue">The value to be added for an absent key</param>
        /// <param name="updateValueFactory">
        ///     The function used to generate a new value for an existing key based on the key's
        ///     existing value
        /// </param>
        /// <returns>
        ///     The new value for the key. This will be either be <paramref name="addValue" /> (if the key was absent) or the
        ///     result of <paramref name="updateValueFactory" /> (if the key was present).
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> or <paramref name="updateValueFactory" /> is <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.OverflowException">
        ///     The dictionary already contains the maximum number of elements (
        ///     <see cref="F:System.Int32.MaxValue" />).
        /// </exception>
        public new TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
        {
            var wasUpdate = false;
            var value = base.AddOrUpdate(key, addValue, (k, v) =>
            {
                wasUpdate = true;
                return updateValueFactory(k, v);
            });

            //TODO: Fix update-notifications -if possible. The following line does not work (NotifyCollectionChangedAction.Replace not allowed)
            //OnCollectionChanged(new NotifyCollectionChangedEventArgs(wasUpdate ? NotifyCollectionChangedAction.Replace : NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return value;
        }

        /// <summary>
        ///     Uses the specified functions and argument to add a key/value pair to the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />
        ///     if the key does not already exist, or to update a key/value pair in the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> if the key already exists.
        /// </summary>
        /// <param name="key">The key to be added or whose value should be updated.</param>
        /// <param name="addValueFactory">The function used to generate a value for an absent key.</param>
        /// <param name="updateValueFactory">
        ///     The function used to generate a new value for an existing key based on the key's
        ///     existing value.
        /// </param>
        /// <param name="factoryArgument">
        ///     An argument to pass into <paramref name="addValueFactory" /> and
        ///     <paramref name="updateValueFactory" />.
        /// </param>
        /// <typeparam name="TArg">
        ///     The type of an argument to pass into <paramref name="addValueFactory" /> and
        ///     <paramref name="updateValueFactory" />.
        /// </typeparam>
        /// <returns>
        ///     The new value for the key. This will be either be the result of <paramref name="addValueFactory" /> (if the
        ///     key was absent) or the result of <paramref name="updateValueFactory" /> (if the key was present).
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" />, <paramref name="addValueFactory" />, or <paramref name="updateValueFactory" /> is a null
        ///     reference (Nothing in Visual Basic).
        /// </exception>
        /// <exception cref="T:System.OverflowException">The dictionary contains too many elements.</exception>
        public new TValue AddOrUpdate<TArg>(TKey key, Func<TKey, TArg, TValue> addValueFactory,
            Func<TKey, TValue, TArg, TValue> updateValueFactory, TArg factoryArgument)
        {
            var wasAdded = false;
            var value = base.AddOrUpdate(key, (k, a) =>
            {
                wasAdded = true;
                return addValueFactory(k, a);
            }, updateValueFactory, factoryArgument);

            //TODO: Fix update-notifications -if possible. The following line does not work (NotifyCollectionChangedAction.Replace not allowed)
            //OnCollectionChanged(new NotifyCollectionChangedEventArgs(wasAdded ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Replace, new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return value;
        }

        /// <summary>
        /// Removes all keys and values from the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />.
        /// </summary>
        public new void Clear()
        {
            base.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        ///     Adds a key/value pair to the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> by using
        ///     the specified function if the key does not already exist. Returns the new value, or the existing value if the key
        ///     exists.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="valueFactory">The function used to generate a value for the key.</param>
        /// <returns>
        ///     The value for the key. This will be either the existing value for the key if the key is already in the
        ///     dictionary, or the new value if the key was not in the dictionary.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> or <paramref name="valueFactory" /> is <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.OverflowException">
        ///     The dictionary already contains the maximum number of elements (
        ///     <see cref="F:System.Int32.MaxValue" />).
        /// </exception>
        public new TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            var wasAdded = false;
            var value = base.GetOrAdd(key, k =>
            {
                wasAdded = true;
                return valueFactory(k);
            });

            if (!wasAdded) return value;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return value;
        }

        /// <summary>
        ///     Adds a key/value pair to the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> if the key
        ///     does not already exist. Returns the new value, or the existing value if the key exists.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="valueToGetOrAdd">The value to be added, if the key does not already exist.</param>
        /// <returns>
        ///     The value for the key. This will be either the existing value for the key if the key is already in the
        ///     dictionary, or the new value if the key was not in the dictionary.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.OverflowException">
        ///     The dictionary already contains the maximum number of elements (
        ///     <see cref="F:System.Int32.MaxValue" />).
        /// </exception>
        public new TValue GetOrAdd(TKey key, TValue valueToGetOrAdd)
        {
            bool isAdd;
            TValue value;
            lock (_padLock)
            {
                isAdd = !ContainsKey(key);
                value = base.GetOrAdd(key, valueToGetOrAdd);
            }

            if (isAdd)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                    new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            }

            return value;
        }

        /// <summary>
        ///     Adds a key/value pair to the <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> by using
        ///     the specified function and an argument if the key does not already exist, or returns the existing value if the key
        ///     exists.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="valueFactory">The function used to generate a value for the key.</param>
        /// <param name="factoryArgument">An argument value to pass into <paramref name="valueFactory" />.</param>
        /// <typeparam name="TArg">The type of an argument to pass into <paramref name="valueFactory" />.</typeparam>
        /// <returns>
        ///     The value for the key. This will be either the existing value for the key if the key is already in the
        ///     dictionary, or the new value if the key was not in the dictionary.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is a <see langword="null" /> reference (Nothing in Visual Basic).
        /// </exception>
        /// <exception cref="T:System.OverflowException">The dictionary contains too many elements.</exception>
        public new TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
        {
            var wasAdded = false;
            var value = base.GetOrAdd(key, (k, a) =>
            {
                wasAdded = true;
                return valueFactory(k, a);
            }, factoryArgument);

            if (!wasAdded) return value;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return value;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        /// <summary>
        ///     Attempts to add the specified key and value to the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add. The value can be  <see langword="null" /> for reference types.</param>
        /// <returns>
        ///     <see langword="true" /> if the key/value pair was added to the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" /> successfully; <see langword="false" /> if the
        ///     key already exists.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is  <see langword="null" />.
        /// </exception>
        /// <exception cref="T:System.OverflowException">
        ///     The dictionary already contains the maximum number of elements (
        ///     <see cref="F:System.Int32.MaxValue" />).
        /// </exception>
        public new bool TryAdd(TKey key, TValue value)
        {
            if (!base.TryAdd(key, value)) return false;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, value), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return true;
        }

        /// <summary>
        ///     Attempts to remove and return the value that has the specified key from the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />.
        /// </summary>
        /// <param name="key">The key of the element to remove and return.</param>
        /// <param name="value">
        ///     When this method returns, contains the object removed from the
        ///     <see cref="T:System.Collections.Concurrent.ConcurrentDictionary`2" />, or the default value of  the
        ///     <see langword="TValue" /> type if <paramref name="key" /> does not exist.
        /// </param>
        /// <returns>
        ///     <see langword="true" /> if the object was removed successfully; otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is  <see langword="null" />.
        /// </exception>
        public new bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (!base.TryRemove(key, out value)) return false;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove,
                new KeyValuePair<TKey, TValue>(key, base[key]), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return true;
        }

        /// <summary>
        ///     Updates the value associated with <paramref name="key" /> to <paramref name="newValue" /> if the existing
        ///     value with <paramref name="key" /> is equal to <paramref name="comparisonValue" />.
        /// </summary>
        /// <param name="key">The key of the value that is compared with <paramref name="comparisonValue" /> and possibly replaced.</param>
        /// <param name="newValue">
        ///     The value that replaces the value of the element that has the specified <paramref name="key" />
        ///     if the comparison results in equality.
        /// </param>
        /// <param name="comparisonValue">
        ///     The value that is compared with the value of the element that has the specified
        ///     <paramref name="key" />.
        /// </param>
        /// <returns>
        ///     <see langword="true" /> if the value with <paramref name="key" /> was equal to <paramref name="comparisonValue" />
        ///     and was replaced with <paramref name="newValue" />; otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///     <paramref name="key" /> is <see langword="null" />.
        /// </exception>
        public new bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
        {
            if (!base.TryUpdate(key, newValue, comparisonValue)) return false;

            //TODO: Fix update-notifications -if possible. The following line does not work with NotifyCollectionChangedAction.Replace
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                new KeyValuePair<TKey, TValue>(key, base[key]), Keys.ToList().IndexOf(key)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

            return true;
        }
    }
}
