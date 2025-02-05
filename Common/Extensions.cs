﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NodaTime;
using Python.Runtime;
using QuantConnect.Orders;
using QuantConnect.Securities;
using Timer = System.Timers.Timer;

namespace QuantConnect
{
    /// <summary>
    /// Extensions function collections - group all static extensions functions here.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Helper method that will cast the provided <see cref="PyObject"/>
        /// to a T type and dispose of it.
        /// </summary>
        /// <typeparam name="T">The target type</typeparam>
        /// <param name="instance">The <see cref="PyObject"/> instance to cast and dispose</param>
        /// <returns>The instance of type T. Will return default value if
        /// provided instance is null</returns>
        public static T GetAndDispose<T>(this PyObject instance)
        {
            if (instance == null)
            {
                return default(T);
            }
            var returnInstance = instance.As<T>();
            // will reduce ref count
            instance.Dispose();
            return returnInstance;
        }

        /// <summary>
        /// Extension to move one element from list from A to position B.
        /// </summary>
        /// <typeparam name="T">Type of list</typeparam>
        /// <param name="list">List we're operating on.</param>
        /// <param name="oldIndex">Index of variable we want to move.</param>
        /// <param name="newIndex">New location for the variable</param>
        public static void Move<T>(this List<T> list, int oldIndex, int newIndex)
        {
            var oItem = list[oldIndex];
            list.RemoveAt(oldIndex);
            if (newIndex > oldIndex) newIndex--;
            list.Insert(newIndex, oItem);
        }

        /// <summary>
        /// Extension method to convert a string into a byte array
        /// </summary>
        /// <param name="str">String to convert to bytes.</param>
        /// <returns>Byte array</returns>
        public static byte[] GetBytes(this string str)
        {
            var bytes = new byte[str.Length * sizeof(char)];
            Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Extentsion method to clear all items from a thread safe queue
        /// </summary>
        /// <remarks>Small risk of race condition if a producer is adding to the list.</remarks>
        /// <typeparam name="T">Queue type</typeparam>
        /// <param name="queue">queue object</param>
        public static void Clear<T>(this ConcurrentQueue<T> queue)
        {
            T item;
            while (queue.TryDequeue(out item)) {
                // NOP
            }
        }

        /// <summary>
        /// Extension method to convert a byte array into a string.
        /// </summary>
        /// <param name="bytes">Byte array to convert.</param>
        /// <param name="encoding">The encoding to use for the conversion. Defaults to Encoding.ASCII</param>
        /// <returns>String from bytes.</returns>
        public static string GetString(this byte[] bytes, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.ASCII;

            return encoding.GetString(bytes);
        }

        /// <summary>
        /// Extension method to convert a string to a MD5 hash.
        /// </summary>
        /// <param name="str">String we want to MD5 encode.</param>
        /// <returns>MD5 hash of a string</returns>
        public static string ToMD5(this string str)
        {
            var builder = new StringBuilder();
            using (var md5Hash = MD5.Create())
            {
                var data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(str));
                foreach (var t in data) builder.Append(t.ToString("x2"));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Encrypt the token:time data to make our API hash.
        /// </summary>
        /// <param name="data">Data to be hashed by SHA256</param>
        /// <returns>Hashed string.</returns>
        public static string ToSHA256(this string data)
        {
            var crypt = new SHA256Managed();
            var hash = new StringBuilder();
            var crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(data), 0, Encoding.UTF8.GetByteCount(data));
            foreach (var theByte in crypto)
            {
                hash.Append(theByte.ToString("x2"));
            }
            return hash.ToString();
        }

        /// <summary>
        /// Lazy string to upper implementation.
        /// Will first verify the string is not already upper and avoid
        /// the call to <see cref="string.ToUpper()"/> if possible.
        /// </summary>
        /// <param name="data">The string to upper</param>
        /// <returns>The upper string</returns>
        public static string LazyToUpper(this string data)
        {
            // for performance only call to upper if required
            var alreadyUpper = true;
            for (int i = 0; i < data.Length && alreadyUpper; i++)
            {
                alreadyUpper = char.IsUpper(data[i]);
            }
            return alreadyUpper ? data : data.ToUpper();
        }

        /// <summary>
        /// Extension method to automatically set the update value to same as "add" value for TryAddUpdate.
        /// This makes the API similar for traditional and concurrent dictionaries.
        /// </summary>
        /// <typeparam name="K">Key type for dictionary</typeparam>
        /// <typeparam name="V">Value type for dictonary</typeparam>
        /// <param name="dictionary">Dictionary object we're operating on</param>
        /// <param name="key">Key we want to add or update.</param>
        /// <param name="value">Value we want to set.</param>
        public static void AddOrUpdate<K, V>(this ConcurrentDictionary<K, V> dictionary, K key, V value)
        {
            dictionary.AddOrUpdate(key, value, (oldkey, oldvalue) => value);
        }

        /// <summary>
        /// Extension method to automatically add/update lazy values in concurrent dictionary.
        /// </summary>
        /// <typeparam name="TKey">Key type for dictionary</typeparam>
        /// <typeparam name="TValue">Value type for dictonary</typeparam>
        /// <param name="dictionary">Dictionary object we're operating on</param>
        /// <param name="key">Key we want to add or update.</param>
        /// <param name="addValueFactory">The function used to generate a value for an absent key</param>
        /// <param name="updateValueFactory">The function used to generate a new value for an existing key based on the key's existing value</param>
        public static TValue AddOrUpdate<TKey, TValue>(this ConcurrentDictionary<TKey, Lazy<TValue>> dictionary, TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            var result = dictionary.AddOrUpdate(key, new Lazy<TValue>(() => addValueFactory(key)), (key2, old) => new Lazy<TValue>(() => updateValueFactory(key2, old.Value)));
            return result.Value;
        }

        /// <summary>
        /// Adds the specified element to the collection with the specified key. If an entry does not exist for th
        /// specified key then one will be created.
        /// </summary>
        /// <typeparam name="TKey">The key type</typeparam>
        /// <typeparam name="TElement">The collection element type</typeparam>
        /// <typeparam name="TCollection">The collection type</typeparam>
        /// <param name="dictionary">The source dictionary to be added to</param>
        /// <param name="key">The key</param>
        /// <param name="element">The element to be added</param>
        public static void Add<TKey, TElement, TCollection>(this IDictionary<TKey, TCollection> dictionary, TKey key, TElement element)
            where TCollection : ICollection<TElement>, new()
        {
            TCollection list;
            if (!dictionary.TryGetValue(key, out list))
            {
                list = new TCollection();
                dictionary.Add(key, list);
            }
            list.Add(element);
        }

        /// <summary>
        /// Extension method to round a double value to a fixed number of significant figures instead of a fixed decimal places.
        /// </summary>
        /// <param name="d">Double we're rounding</param>
        /// <param name="digits">Number of significant figures</param>
        /// <returns>New double rounded to digits-significant figures</returns>
        public static double RoundToSignificantDigits(this double d, int digits)
        {
            if (d == 0) return 0;
            var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(d))) + 1);
            return scale * Math.Round(d / scale, digits);
        }

        /// <summary>
        /// Extension method to round a double value to a fixed number of significant figures instead of a fixed decimal places.
        /// </summary>
        /// <param name="d">Double we're rounding</param>
        /// <param name="digits">Number of significant figures</param>
        /// <returns>New double rounded to digits-significant figures</returns>
        public static decimal RoundToSignificantDigits(this decimal d, int digits)
        {
            if (d == 0) return 0;
            var scale = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double) Math.Abs(d))) + 1);
            return scale * Math.Round(d / scale, digits);
        }

        /// <summary>
        /// Will truncate the provided decimal, without rounding, to 3 decimal places
        /// </summary>
        /// <param name="value">The value to truncate</param>
        /// <returns>New instance with just 3 decimal places</returns>
        public static decimal TruncateTo3DecimalPlaces(this decimal value)
        {
            if (value == decimal.MaxValue
                || value == decimal.MinValue
                || value == 0)
            {
                return value;
            }
            return Math.Truncate(1000 * value) / 1000;
        }

        /// <summary>
        /// Provides global smart rounding, numbers larger than 1000 will round to 4 decimal places,
        /// while numbers smaller will round to 7 significant digits
        /// </summary>
        public static decimal SmartRounding(this decimal input)
        {
            input = Normalize(input);

            // any larger numbers we still want some decimal places
            if (input > 1000)
            {
                return Math.Round(input, 4);
            }

            // this is good for forex and other small numbers
            var d = (double)input;
            return (decimal)d.RoundToSignificantDigits(7);
        }

        /// <summary>
        /// Casts the specified input value to a decimal while acknowledging the overflow conditions
        /// </summary>
        /// <param name="input">The value to be cast</param>
        /// <returns>The input value as a decimal, if the value is too large or to small to be represented
        /// as a decimal, then the closest decimal value will be returned</returns>
        public static decimal SafeDecimalCast(this double input)
        {
            if (input.IsNaNOrZero()) return 0;
            if (input <= (double) decimal.MinValue) return decimal.MinValue;
            if (input >= (double) decimal.MaxValue) return decimal.MaxValue;
            return (decimal) input;
        }

        /// <summary>
        /// Will remove any trailing zeros for the provided decimal input
        /// </summary>
        /// <param name="input">The <see cref="decimal"/> to remove trailing zeros from</param>
        /// <returns>Provided input with no trailing zeros</returns>
        /// <remarks>Will not have the expected behavior when called from Python,
        /// since the returned <see cref="decimal"/> will be converted to python float,
        /// <see cref="NormalizeToStr"/></remarks>
        public static decimal Normalize(this decimal input)
        {
            // http://stackoverflow.com/a/7983330/1582922
            return input / 1.000000000000000000000000000000000m;
        }

        /// <summary>
        /// Will remove any trailing zeros for the provided decimal and convert to string.
        /// Uses <see cref="Normalize"/>.
        /// </summary>
        /// <param name="input">The <see cref="decimal"/> to convert to <see cref="string"/></param>
        /// <returns>Input converted to <see cref="string"/> with no trailing zeros</returns>
        public static string NormalizeToStr(this decimal input)
        {
            return Normalize(input).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Extension method for faster string to decimal conversion.
        /// </summary>
        /// <param name="str">String to be converted to positive decimal value</param>
        /// <remarks>
        /// Leading and trailing whitespace chars are ignored
        /// </remarks>
        /// <returns>Decimal value of the string</returns>
        public static decimal ToDecimal(this string str)
        {
            long value = 0;
            var decimalPlaces = 0;
            var hasDecimals = false;
            var index = 0;
            var length = str.Length;

            while (index < length && char.IsWhiteSpace(str[index]))
            {
                index++;
            }

            var isNegative = index < length && str[index] == '-';
            if (isNegative)
            {
                index++;
            }

            while (index < length)
            {
                var ch = str[index++];
                if (ch == '.')
                {
                    hasDecimals = true;
                    decimalPlaces = 0;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    break;
                }
                else
                {
                    value = value * 10 + (ch - '0');
                    decimalPlaces++;
                }
            }

            var lo = (int)value;
            var mid = (int)(value >> 32);
            return new decimal(lo, mid, 0, isNegative, (byte)(hasDecimals ? decimalPlaces : 0));
        }

        /// <summary>
        /// Extension method for faster string to Int32 conversion.
        /// </summary>
        /// <param name="str">String to be converted to positive Int32 value</param>
        /// <remarks>Method makes some assuptions - always numbers, no "signs" +,- etc.</remarks>
        /// <returns>Int32 value of the string</returns>
        public static int ToInt32(this string str)
        {
            int value = 0;
            for (var i = 0; i < str.Length; i++)
            {
                if (str[i] == '.')
                    break;

                value = value * 10 + (str[i] - '0');
            }
            return value;
        }

        /// <summary>
        /// Extension method for faster string to Int64 conversion.
        /// </summary>
        /// <param name="str">String to be converted to positive Int64 value</param>
        /// <remarks>Method makes some assuptions - always numbers, no "signs" +,- etc.</remarks>
        /// <returns>Int32 value of the string</returns>
        public static long ToInt64(this string str)
        {
            long value = 0;
            for (var i = 0; i < str.Length; i++)
            {
                if (str[i] == '.')
                    break;

                value = value * 10 + (str[i] - '0');
            }
            return value;
        }

        /// <summary>
        /// Breaks the specified string into csv components, all commas are considered separators
        /// </summary>
        /// <param name="str">The string to be broken into csv</param>
        /// <param name="size">The expected size of the output list</param>
        /// <returns>A list of the csv pieces</returns>
        public static List<string> ToCsv(this string str, int size = 4)
        {
            int last = 0;
            var csv = new List<string>(size);
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == ',')
                {
                    if (last != 0) last = last + 1;
                    csv.Add(str.Substring(last, i - last));
                    last = i;
                }
            }
            if (last != 0) last = last + 1;
            csv.Add(str.Substring(last));
            return csv;
        }

        /// <summary>
        /// Breaks the specified string into csv components, works correctly with commas in data fields
        /// </summary>
        /// <param name="str">The string to be broken into csv</param>
        /// <param name="size">The expected size of the output list</param>
        /// <returns>A list of the csv pieces</returns>
        public static List<string> ToCsvData(this string str, int size = 4)
        {
            var csv = new List<string>(size);

            int last = -1;
            bool textDataField = false;

            for (var i = 0; i < str.Length; i++)
            {
                switch (str[i])
                {
                    case '"':
                        textDataField = !textDataField;
                        break;
                    case ',':
                        if (!textDataField)
                        {
                            csv.Add(str.Substring(last + 1, (i - last)).Trim(' ', ','));
                            last = i;
                        }
                        break;
                    default:
                        break;
                }
            }

            if (last != str.Length - 1)
            {
                csv.Add(str.Substring(last + 1).Trim());
            }

            return csv;
        }

        /// <summary>
        /// Check if a number is NaN or equal to zero
        /// </summary>
        /// <param name="value">The double value to check</param>
        public static bool IsNaNOrZero(this double value)
        {
            return double.IsNaN(value) || Math.Abs(value) < double.Epsilon;
        }

        /// <summary>
        /// Gets the smallest positive number that can be added to a decimal instance and return
        /// a new value that does not == the old value
        /// </summary>
        public static decimal GetDecimalEpsilon()
        {
            return new decimal(1, 0, 0, false, 27); //1e-27m;
        }

        /// <summary>
        /// Extension method to extract the extension part of this file name if it matches a safe list, or return a ".custom" extension for ones which do not match.
        /// </summary>
        /// <param name="str">String we're looking for the extension for.</param>
        /// <returns>Last 4 character string of string.</returns>
        public static string GetExtension(this string str) {
            var ext = str.Substring(Math.Max(0, str.Length - 4));
            var allowedExt = new List<string>() { ".zip", ".csv", ".json" };
            if (!allowedExt.Contains(ext))
            {
                ext = ".custom";
            }
            return ext;
        }

        /// <summary>
        /// Extension method to convert strings to stream to be read.
        /// </summary>
        /// <param name="str">String to convert to stream</param>
        /// <returns>Stream instance</returns>
        public static Stream ToStream(this string str)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// Extension method to round a timeSpan to nearest timespan period.
        /// </summary>
        /// <param name="time">TimeSpan To Round</param>
        /// <param name="roundingInterval">Rounding Unit</param>
        /// <param name="roundingType">Rounding method</param>
        /// <returns>Rounded timespan</returns>
        public static TimeSpan Round(this TimeSpan time, TimeSpan roundingInterval, MidpointRounding roundingType)
        {
            if (roundingInterval == TimeSpan.Zero)
            {
                // divide by zero exception
                return time;
            }

            return new TimeSpan(
                Convert.ToInt64(Math.Round(
                    time.Ticks / (decimal)roundingInterval.Ticks,
                    roundingType
                )) * roundingInterval.Ticks
            );
        }


        /// <summary>
        /// Extension method to round timespan to nearest timespan period.
        /// </summary>
        /// <param name="time">Base timespan we're looking to round.</param>
        /// <param name="roundingInterval">Timespan period we're rounding.</param>
        /// <returns>Rounded timespan period</returns>
        public static TimeSpan Round(this TimeSpan time, TimeSpan roundingInterval)
        {
            return Round(time, roundingInterval, MidpointRounding.ToEven);
        }

        /// <summary>
        /// Extension method to round a datetime down by a timespan interval.
        /// </summary>
        /// <param name="dateTime">Base DateTime object we're rounding down.</param>
        /// <param name="interval">Timespan interval to round to.</param>
        /// <returns>Rounded datetime</returns>
        public static DateTime RoundDown(this DateTime dateTime, TimeSpan interval)
        {
            if (interval == TimeSpan.Zero)
            {
                // divide by zero exception
                return dateTime;
            }

            var amount = dateTime.Ticks % interval.Ticks;
            if (amount > 0)
            {
                return dateTime.AddTicks(-amount);
            }
            return dateTime;
        }

        /// <summary>
        /// Rounds the specified date time in the specified time zone. Careful with calling this method in a loop while modifying dateTime, check unit tests.
        /// </summary>
        /// <param name="dateTime">Date time to be rounded</param>
        /// <param name="roundingInterval">Timespan rounding period</param>
        /// <param name="sourceTimeZone">Time zone of the date time</param>
        /// <param name="roundingTimeZone">Time zone in which the rounding is performed</param>
        /// <returns>The rounded date time in the source time zone</returns>
        public static DateTime RoundDownInTimeZone(this DateTime dateTime, TimeSpan roundingInterval, DateTimeZone sourceTimeZone, DateTimeZone roundingTimeZone)
        {
            var dateTimeInRoundingTimeZone = dateTime.ConvertTo(sourceTimeZone, roundingTimeZone);
            var roundedDateTimeInRoundingTimeZone = dateTimeInRoundingTimeZone.RoundDown(roundingInterval);
            return roundedDateTimeInRoundingTimeZone.ConvertTo(roundingTimeZone, sourceTimeZone);
        }

        /// <summary>
        /// Extension method to round a datetime down by a timespan interval until it's
        /// within the specified exchange's open hours. This works by first rounding down
        /// the specified time using the interval, then producing a bar between that
        /// rounded time and the interval plus the rounded time and incrementally walking
        /// backwards until the exchange is open
        /// </summary>
        /// <param name="dateTime">Time to be rounded down</param>
        /// <param name="interval">Timespan interval to round to.</param>
        /// <param name="exchangeHours">The exchange hours to determine open times</param>
        /// <param name="extendedMarket">True for extended market hours, otherwise false</param>
        /// <returns>Rounded datetime</returns>
        public static DateTime ExchangeRoundDown(this DateTime dateTime, TimeSpan interval, SecurityExchangeHours exchangeHours, bool extendedMarket)
        {
            // can't round against a zero interval
            if (interval == TimeSpan.Zero) return dateTime;

            var rounded = dateTime.RoundDown(interval);
            while (!exchangeHours.IsOpen(rounded, rounded + interval, extendedMarket))
            {
                rounded -= interval;
            }
            return rounded;
        }

        /// <summary>
        /// Extension method to round a datetime down by a timespan interval until it's
        /// within the specified exchange's open hours. The rounding is performed in the
        /// specified time zone
        /// </summary>
        /// <param name="dateTime">Time to be rounded down</param>
        /// <param name="interval">Timespan interval to round to.</param>
        /// <param name="exchangeHours">The exchange hours to determine open times</param>
        /// <param name="roundingTimeZone">The time zone to perform the rounding in</param>
        /// <param name="extendedMarket">True for extended market hours, otherwise false</param>
        /// <returns>Rounded datetime</returns>
        public static DateTime ExchangeRoundDownInTimeZone(this DateTime dateTime, TimeSpan interval, SecurityExchangeHours exchangeHours, DateTimeZone roundingTimeZone, bool extendedMarket)
        {
            // can't round against a zero interval
            if (interval == TimeSpan.Zero) return dateTime;

            var dateTimeInRoundingTimeZone = dateTime.ConvertTo(exchangeHours.TimeZone, roundingTimeZone);
            var roundedDateTimeInRoundingTimeZone = dateTimeInRoundingTimeZone.RoundDown(interval);
            var rounded = roundedDateTimeInRoundingTimeZone.ConvertTo(roundingTimeZone, exchangeHours.TimeZone);

            while (!exchangeHours.IsOpen(rounded, rounded + interval, extendedMarket))
            {
                // Will subtract interval to 'dateTime' in the roundingTimeZone (using the same value type instance) to avoid issues with daylight saving time changes.
                // GH issue 2368: subtracting interval to 'dateTime' in exchangeHours.TimeZone and converting back to roundingTimeZone
                // caused the substraction to be neutralized by daylight saving time change, which caused an infinite loop situation in this loop.
                // The issue also happens if substracting in roundingTimeZone and converting back to exchangeHours.TimeZone.

                dateTimeInRoundingTimeZone -= interval;
                roundedDateTimeInRoundingTimeZone = dateTimeInRoundingTimeZone.RoundDown(interval);
                rounded = roundedDateTimeInRoundingTimeZone.ConvertTo(roundingTimeZone, exchangeHours.TimeZone);
            }
            return rounded;
        }

        /// <summary>
        /// Extension method to round a datetime to the nearest unit timespan.
        /// </summary>
        /// <param name="datetime">Datetime object we're rounding.</param>
        /// <param name="roundingInterval">Timespan rounding period.</param>
        /// <returns>Rounded datetime</returns>
        public static DateTime Round(this DateTime datetime, TimeSpan roundingInterval)
        {
            return new DateTime((datetime - DateTime.MinValue).Round(roundingInterval).Ticks);
        }

        /// <summary>
        /// Extension method to explicitly round up to the nearest timespan interval.
        /// </summary>
        /// <param name="time">Base datetime object to round up.</param>
        /// <param name="d">Timespan interval for rounding</param>
        /// <returns>Rounded datetime</returns>
        public static DateTime RoundUp(this DateTime time, TimeSpan d)
        {
            if (d == TimeSpan.Zero)
            {
                // divide by zero exception
                return time;
            }
            return new DateTime(((time.Ticks + d.Ticks - 1) / d.Ticks) * d.Ticks);
        }

        /// <summary>
        /// Converts the specified time from the <paramref name="from"/> time zone to the <paramref name="to"/> time zone
        /// </summary>
        /// <param name="time">The time to be converted in terms of the <paramref name="from"/> time zone</param>
        /// <param name="from">The time zone the specified <paramref name="time"/> is in</param>
        /// <param name="to">The time zone to be converted to</param>
        /// <param name="strict">True for strict conversion, this will throw during ambiguitities, false for lenient conversion</param>
        /// <returns>The time in terms of the to time zone</returns>
        public static DateTime ConvertTo(this DateTime time, DateTimeZone from, DateTimeZone to, bool strict = false)
        {
            if (strict)
            {
                return from.AtStrictly(LocalDateTime.FromDateTime(time)).WithZone(to).ToDateTimeUnspecified();
            }

            return from.AtLeniently(LocalDateTime.FromDateTime(time)).WithZone(to).ToDateTimeUnspecified();
        }

        /// <summary>
        /// Converts the specified time from UTC to the <paramref name="to"/> time zone
        /// </summary>
        /// <param name="time">The time to be converted expressed in UTC</param>
        /// <param name="to">The destinatio time zone</param>
        /// <param name="strict">True for strict conversion, this will throw during ambiguitities, false for lenient conversion</param>
        /// <returns>The time in terms of the <paramref name="to"/> time zone</returns>
        public static DateTime ConvertFromUtc(this DateTime time, DateTimeZone to, bool strict = false)
        {
            return time.ConvertTo(TimeZones.Utc, to, strict);
        }

        /// <summary>
        /// Converts the specified time from the <paramref name="from"/> time zone to <see cref="TimeZones.Utc"/>
        /// </summary>
        /// <param name="time">The time to be converted in terms of the <paramref name="from"/> time zone</param>
        /// <param name="from">The time zone the specified <paramref name="time"/> is in</param>
        /// <param name="strict">True for strict conversion, this will throw during ambiguitities, false for lenient conversion</param>
        /// <returns>The time in terms of the to time zone</returns>
        public static DateTime ConvertToUtc(this DateTime time, DateTimeZone from, bool strict = false)
        {
            if (strict)
            {
                return from.AtStrictly(LocalDateTime.FromDateTime(time)).ToDateTimeUtc();
            }

            return from.AtLeniently(LocalDateTime.FromDateTime(time)).ToDateTimeUtc();
        }

        /// <summary>
        /// Business day here is defined as any day of the week that is not saturday or sunday
        /// </summary>
        /// <param name="date">The date to be examined</param>
        /// <returns>A bool indicating wether the datetime is a weekday or not</returns>
        public static bool IsCommonBusinessDay(this DateTime date)
        {
            return (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday);
        }

        /// <summary>
        /// Add the reset method to the System.Timer class.
        /// </summary>
        /// <param name="timer">System.timer object</param>
        public static void Reset(this Timer timer)
        {
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        /// Function used to match a type against a string type name. This function compares on the AssemblyQualfiedName,
        /// the FullName, and then just the Name of the type.
        /// </summary>
        /// <param name="type">The type to test for a match</param>
        /// <param name="typeName">The name of the type to match</param>
        /// <returns>True if the specified type matches the type name, false otherwise</returns>
        public static bool MatchesTypeName(this Type type, string typeName)
        {
            if (type.AssemblyQualifiedName == typeName)
            {
                return true;
            }
            if (type.FullName == typeName)
            {
                return true;
            }
            if (type.Name == typeName)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks the specified type to see if it is a subclass of the <paramref name="possibleSuperType"/>. This method will
        /// crawl up the inheritance heirarchy to check for equality using generic type definitions (if exists)
        /// </summary>
        /// <param name="type">The type to be checked as a subclass of <paramref name="possibleSuperType"/></param>
        /// <param name="possibleSuperType">The possible superclass of <paramref name="type"/></param>
        /// <returns>True if <paramref name="type"/> is a subclass of the generic type definition <paramref name="possibleSuperType"/></returns>
        public static bool IsSubclassOfGeneric(this Type type, Type possibleSuperType)
        {
            while (type != null && type != typeof(object))
            {
                Type cur;
                if (type.IsGenericType && possibleSuperType.IsGenericTypeDefinition)
                {
                    cur = type.GetGenericTypeDefinition();
                }
                else
                {
                    cur = type;
                }
                if (possibleSuperType == cur)
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Gets a type's name with the generic parameters filled in the way they would look when
        /// defined in code, such as converting Dictionary&lt;`1,`2&gt; to Dictionary&lt;string,int&gt;
        /// </summary>
        /// <param name="type">The type who's name we seek</param>
        /// <returns>A better type name</returns>
        public static string GetBetterTypeName(this Type type)
        {
            string name = type.Name;
            if (type.IsGenericType)
            {
                var genericArguments = type.GetGenericArguments();
                var toBeReplaced = "`" + (genericArguments.Length);
                name = name.Replace(toBeReplaced, "<" + string.Join(", ", genericArguments.Select(x => x.GetBetterTypeName())) + ">");
            }
            return name;
        }

        /// <summary>
        /// Converts the Resolution instance into a TimeSpan instance
        /// </summary>
        /// <param name="resolution">The resolution to be converted</param>
        /// <returns>A TimeSpan instance that represents the resolution specified</returns>
        public static TimeSpan ToTimeSpan(this Resolution resolution)
        {
            switch (resolution)
            {
                case Resolution.Tick:
                    // ticks can be instantaneous
                    return TimeSpan.FromTicks(0);
                case Resolution.Second:
                    return TimeSpan.FromSeconds(1);
                case Resolution.Minute:
                    return TimeSpan.FromMinutes(1);
                case Resolution.Hour:
                    return TimeSpan.FromHours(1);
                case Resolution.Daily:
                    return TimeSpan.FromDays(1);
                default:
                    throw new ArgumentOutOfRangeException("resolution");
            }
        }

        /// <summary>
        /// Converts the specified time span into a resolution enum value. If an exact match
        /// is not found and `requireExactMatch` is false, then the higher resoluion will be
        /// returned. For example, timeSpan=5min will return Minute resolution.
        /// </summary>
        /// <param name="timeSpan">The time span to convert to resolution</param>
        /// <param name="requireExactMatch">True to throw an exception if an exact match is not found</param>
        /// <returns>The resolution</returns>
        public static Resolution ToHigherResolutionEquivalent(this TimeSpan timeSpan, bool requireExactMatch)
        {
            if (requireExactMatch)
            {
                if (TimeSpan.Zero == timeSpan)  return Resolution.Tick;
                if (Time.OneSecond == timeSpan) return Resolution.Second;
                if (Time.OneMinute == timeSpan) return Resolution.Minute;
                if (Time.OneHour   == timeSpan) return Resolution.Hour;
                if (Time.OneDay    == timeSpan) return Resolution.Daily;
                throw new InvalidOperationException($"Unable to exactly convert time span ('{timeSpan}') to resolution.");
            }

            // for non-perfect matches
            if (Time.OneSecond > timeSpan) return Resolution.Tick;
            if (Time.OneMinute > timeSpan) return Resolution.Second;
            if (Time.OneHour   > timeSpan) return Resolution.Minute;
            if (Time.OneDay    > timeSpan) return Resolution.Hour;

            return Resolution.Daily;
        }

        /// <summary>
        /// Converts the specified string value into the specified type
        /// </summary>
        /// <typeparam name="T">The output type</typeparam>
        /// <param name="value">The string value to be converted</param>
        /// <returns>The converted value</returns>
        public static T ConvertTo<T>(this string value)
        {
            return (T) value.ConvertTo(typeof (T));
        }

        /// <summary>
        /// Converts the specified string value into the specified type
        /// </summary>
        /// <param name="value">The string value to be converted</param>
        /// <param name="type">The output type</param>
        /// <returns>The converted value</returns>
        public static object ConvertTo(this string value, Type type)
        {
            if (type.IsEnum)
            {
                return Enum.Parse(type, value);
            }

            if (typeof (IConvertible).IsAssignableFrom(type))
            {
                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }

            // try and find a static parse method
            var parse = type.GetMethod("Parse", new[] {typeof (string)});
            if (parse != null)
            {
                var result = parse.Invoke(null, new object[] {value});
                return result;
            }

            return JsonConvert.DeserializeObject(value, type);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="T:System.Threading.WaitHandle"/> receives a signal, while observing a <see cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        /// <param name="waitHandle">The wait handle to wait on</param>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to observe.</param>
        /// <exception cref="T:System.InvalidOperationException">The maximum number of waiters has been exceeded.</exception>
        /// <exception cref="T:System.OperationCanceledExcepton"><paramref name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The object has already been disposed or the <see cref="T:System.Threading.CancellationTokenSource"/> that created <paramref name="cancellationToken"/> has been disposed.</exception>
        public static bool WaitOne(this WaitHandle waitHandle, CancellationToken cancellationToken)
        {
            return waitHandle.WaitOne(Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="T:System.Threading.WaitHandle"/> is set, using a <see cref="T:System.TimeSpan"/> to measure the time interval, while observing a <see cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        ///
        /// <returns>
        /// true if the <see cref="T:System.Threading.WaitHandle"/> was set; otherwise, false.
        /// </returns>
        /// <param name="waitHandle">The wait handle to wait on</param>
        /// <param name="timeout">A <see cref="T:System.TimeSpan"/> that represents the number of milliseconds to wait, or a <see cref="T:System.TimeSpan"/> that represents -1 milliseconds to wait indefinitely.</param>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to observe.</param>
        /// <exception cref="T:System.Threading.OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater than <see cref="F:System.Int32.MaxValue"/>.</exception>
        /// <exception cref="T:System.InvalidOperationException">The maximum number of waiters has been exceeded. </exception><exception cref="T:System.ObjectDisposedException">The object has already been disposed or the <see cref="T:System.Threading.CancellationTokenSource"/> that created <paramref name="cancellationToken"/> has been disposed.</exception>
        public static bool WaitOne(this WaitHandle waitHandle, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return waitHandle.WaitOne((int) timeout.TotalMilliseconds, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="T:System.Threading.WaitHandle"/> is set, using a 32-bit signed integer to measure the time interval, while observing a <see cref="T:System.Threading.CancellationToken"/>.
        /// </summary>
        ///
        /// <returns>
        /// true if the <see cref="T:System.Threading.WaitHandle"/> was set; otherwise, false.
        /// </returns>
        /// <param name="waitHandle">The wait handle to wait on</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="F:System.Threading.Timeout.Infinite"/>(-1) to wait indefinitely.</param>
        /// <param name="cancellationToken">The <see cref="T:System.Threading.CancellationToken"/> to observe.</param>
        /// <exception cref="T:System.Threading.OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a negative number other than -1, which represents an infinite time-out.</exception>
        /// <exception cref="T:System.InvalidOperationException">The maximum number of waiters has been exceeded.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The object has already been disposed or the <see cref="T:System.Threading.CancellationTokenSource"/> that created <paramref name="cancellationToken"/> has been disposed.</exception>
        public static bool WaitOne(this WaitHandle waitHandle, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            return WaitHandle.WaitAny(new[] { waitHandle, cancellationToken.WaitHandle }, millisecondsTimeout) == 0;
        }

        /// <summary>
        /// Gets the MD5 hash from a stream
        /// </summary>
        /// <param name="stream">The stream to compute a hash for</param>
        /// <returns>The MD5 hash</returns>
        public static byte[] GetMD5Hash(this Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(stream);
            }
        }

        /// <summary>
        /// Convert a string into the same string with a URL! :)
        /// </summary>
        /// <param name="source">The source string to be converted</param>
        /// <returns>The same source string but with anchor tags around substrings matching a link regex</returns>
        public static string WithEmbeddedHtmlAnchors(this string source)
        {
            var regx = new Regex("http(s)?://([\\w+?\\.\\w+])+([a-zA-Z0-9\\~\\!\\@\\#\\$\\%\\^\\&amp;\\*\\(\\)_\\-\\=\\+\\\\\\/\\?\\.\\:\\;\\'\\,]*([a-zA-Z0-9\\?\\#\\=\\/]){1})?", RegexOptions.IgnoreCase);
            var matches = regx.Matches(source);
            foreach (Match match in matches)
            {
                source = source.Replace(match.Value, "<a href='" + match.Value + "' target='blank'>" + match.Value + "</a>");
            }
            return source;
        }

        /// <summary>
        /// Get the first occurence of a string between two characters from another string
        /// </summary>
        /// <param name="value">The original string</param>
        /// <param name="left">Left bound of the substring</param>
        /// <param name="right">Right bound of the substring</param>
        /// <returns>Substring from original string bounded by the two characters</returns>
        public static string GetStringBetweenChars(this string value, char left, char right)
        {
            var startIndex = 1 + value.IndexOf(left);
            var length = value.IndexOf(right, startIndex) - startIndex;
            if (length > 0)
            {
                value = value.Substring(startIndex, length);
                startIndex = 1 + value.IndexOf(left);
                return value.Substring(startIndex).Trim();
            }
            return string.Empty;
        }

        /// <summary>
        /// Return the first in the series of names, or find the one that matches the configured algirithmTypeName
        /// </summary>
        /// <param name="names">The list of class names</param>
        /// <param name="algorithmTypeName">The configured algorithm type name from the config</param>
        /// <returns>The name of the class being run</returns>
        public static string SingleOrAlgorithmTypeName(this List<string> names, string algorithmTypeName)
        {
            // if there's only one use that guy
            // if there's more than one then find which one we should use using the algorithmTypeName specified
            return names.Count == 1 ? names.Single() : names.SingleOrDefault(x => x.EndsWith("." + algorithmTypeName));
        }

        /// <summary>
        /// Converts the specified <paramref name="enum"/> value to its corresponding lower-case string representation
        /// </summary>
        /// <param name="enum">The enumeration value</param>
        /// <returns>A lower-case string representation of the specified enumeration value</returns>
        public static string ToLower(this Enum @enum)
        {
            return @enum.ToString().ToLower();
        }

        /// <summary>
        /// Converts the specified <paramref name="securityType"/> value to its corresponding lower-case string representation
        /// </summary>
        /// <remarks>This method provides faster performance than <see cref="ToLower"/></remarks>
        /// <param name="securityType">The SecurityType value</param>
        /// <returns>A lower-case string representation of the specified SecurityType value</returns>
        public static string SecurityTypeToLower(this SecurityType securityType)
        {
            switch (securityType)
            {
                case SecurityType.Base:
                    return "base";
                case SecurityType.Equity:
                    return "equity";
                case SecurityType.Option:
                    return "option";
                case SecurityType.Commodity:
                    return "commodity";
                case SecurityType.Forex:
                    return "forex";
                case SecurityType.Future:
                    return "future";
                case SecurityType.Cfd:
                    return "cfd";
                case SecurityType.Crypto:
                    return "crypto";
                default:
                    // just in case
                    return securityType.ToLower();
            }
        }

        /// <summary>
        /// Converts the specified <paramref name="tickType"/> value to its corresponding lower-case string representation
        /// </summary>
        /// <remarks>This method provides faster performance than <see cref="ToLower"/></remarks>
        /// <param name="tickType">The tickType value</param>
        /// <returns>A lower-case string representation of the specified tickType value</returns>
        public static string TickTypeToLower(this TickType tickType)
        {
            switch (tickType)
            {
                case TickType.Trade:
                    return "trade";
                case TickType.Quote:
                    return "quote";
                case TickType.OpenInterest:
                    return "openinterest";
                default:
                    // just in case
                    return tickType.ToLower();
            }
        }

        /// <summary>
        /// Converts the specified <paramref name="resolution"/> value to its corresponding lower-case string representation
        /// </summary>
        /// <remarks>This method provides faster performance than <see cref="ToLower"/></remarks>
        /// <param name="resolution">The resolution value</param>
        /// <returns>A lower-case string representation of the specified resolution value</returns>
        public static string ResolutionToLower(this Resolution resolution)
        {
            switch (resolution)
            {
                case Resolution.Tick:
                    return "tick";
                case Resolution.Second:
                    return "second";
                case Resolution.Minute:
                    return "minute";
                case Resolution.Hour:
                    return "hour";
                case Resolution.Daily:
                    return "daily";
                default:
                    // just in case
                    return resolution.ToLower();
            }
        }

        /// <summary>
        /// Turn order into an order ticket
        /// </summary>
        /// <param name="order">The <see cref="Order"/> being converted</param>
        /// <param name="transactionManager">The transaction manager, <see cref="SecurityTransactionManager"/></param>
        /// <returns></returns>
        public static OrderTicket ToOrderTicket(this Order order, SecurityTransactionManager transactionManager)
        {
            var limitPrice = 0m;
            var stopPrice = 0m;

            switch (order.Type)
            {
                case OrderType.Limit:
                    var limitOrder = order as LimitOrder;
                    limitPrice = limitOrder.LimitPrice;
                    break;
                case OrderType.StopMarket:
                    var stopMarketOrder = order as StopMarketOrder;
                    stopPrice = stopMarketOrder.StopPrice;
                    break;
                case OrderType.StopLimit:
                    var stopLimitOrder = order as StopLimitOrder;
                    stopPrice = stopLimitOrder.StopPrice;
                    limitPrice = stopLimitOrder.LimitPrice;
                    break;
                case OrderType.OptionExercise:
                case OrderType.Market:
                case OrderType.MarketOnOpen:
                case OrderType.MarketOnClose:
                    limitPrice = order.Price;
                    stopPrice = order.Price;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var submitOrderRequest = new SubmitOrderRequest(order.Type,
                order.SecurityType,
                order.Symbol,
                order.Quantity,
                stopPrice,
                limitPrice,
                order.Time,
                order.Tag,
                order.Properties);

            submitOrderRequest.SetOrderId(order.Id);
            var orderTicket = new OrderTicket(transactionManager, submitOrderRequest);
            orderTicket.SetOrder(order);
            return orderTicket;
        }

        public static void ProcessUntilEmpty<T>(this IProducerConsumerCollection<T> collection, Action<T> handler)
        {
            T item;
            while (collection.TryTake(out item))
            {
                handler(item);
            }
        }

        /// <summary>
        /// Returns a <see cref="string"/> that represents the current <see cref="PyObject"/>
        /// </summary>
        /// <param name="pyObject">The <see cref="PyObject"/> being converted</param>
        /// <returns>string that represents the current PyObject</returns>
        public static string ToSafeString(this PyObject pyObject)
        {
            using (Py.GIL())
            {
                var value = "";
                // PyObject objects that have the to_string method, like some pandas objects,
                // can use this method to convert them into string objects
                if (pyObject.HasAttr("to_string"))
                {
                    var pyValue = pyObject.InvokeMethod("to_string");
                    value = Environment.NewLine + pyValue;
                    pyValue.Dispose();
                }
                else
                {
                    value = pyObject.ToString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        var pythonType = pyObject.GetPythonType();
                        if (pythonType.GetType() == typeof(PyObject))
                        {
                            value = pythonType.ToString();
                        }
                        else
                        {
                            var type = pythonType.As<Type>();
                            value = pyObject.AsManagedObject(type).ToString();
                        }
                        pythonType.Dispose();
                    }
                }
                return value;
            }
        }

        /// <summary>
        /// Tries to convert a <see cref="PyObject"/> into a managed object
        /// </summary>
        /// <typeparam name="T">Target type of the resulting managed object</typeparam>
        /// <param name="pyObject">PyObject to be converted</param>
        /// <param name="result">Managed object </param>
        /// <returns>True if successful conversion</returns>
        public static bool TryConvert<T>(this PyObject pyObject, out T result)
        {
            result = default(T);
            var type = typeof(T);

            if (pyObject == null)
            {
                return true;
            }

            using (Py.GIL())
            {
                try
                {
                    // Special case: Type
                    if (typeof(Type).IsAssignableFrom(type))
                    {
                        result = (T)pyObject.AsManagedObject(type);
                        return true;
                    }

                    // Special case: IEnumerable
                    if (typeof(IEnumerable).IsAssignableFrom(type))
                    {
                        result = (T)pyObject.AsManagedObject(type);
                        return true;
                    }

                    var pythonType = pyObject.GetPythonType();
                    var csharpType = pythonType.As<Type>();

                    if (!type.IsAssignableFrom(csharpType))
                    {
                        pythonType.Dispose();
                        return false;
                    }

                    result = (T)pyObject.AsManagedObject(type);

                    // If the PyObject type and the managed object names are the same,
                    // pyObject is a C# object wrapped in PyObject, in this case return true
                    // Otherwise, pyObject is a python object that subclass a C# class.
                    var name = (((dynamic) pythonType).__name__ as PyObject).GetAndDispose<string>();
                    pythonType.Dispose();
                    return name == result.GetType().Name;
                }
                catch
                {
                    // Do not throw or log the exception.
                    // Return false as an exception means that the conversion could not be made.
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to convert a <see cref="PyObject"/> into a managed object
        /// </summary>
        /// <typeparam name="T">Target type of the resulting managed object</typeparam>
        /// <param name="pyObject">PyObject to be converted</param>
        /// <param name="result">Managed object </param>
        /// <returns>True if successful conversion</returns>
        public static bool TryConvertToDelegate<T>(this PyObject pyObject, out T result)
        {
            var type = typeof(T);

            if (!typeof(MulticastDelegate).IsAssignableFrom(type))
            {
                throw new ArgumentException($"TryConvertToDelegate cannot be used to convert a PyObject into {type}.");
            }

            result = default(T);

            if (pyObject == null)
            {
                return true;
            }

            var code = string.Empty;
            var types = type.GetGenericArguments();

            using (Py.GIL())
            {
                var locals = new PyDict();
                try
                {
                    for (var i = 0; i < types.Length; i++)
                    {
                        code += $",t{i}";
                        locals.SetItem($"t{i}", types[i].ToPython());
                    }

                    locals.SetItem("pyObject", pyObject);

                    var name = type.FullName.Substring(0, type.FullName.IndexOf('`'));
                    code = $"import System; delegate = {name}[{code.Substring(1)}](pyObject)";

                    PythonEngine.Exec(code, null, locals.Handle);
                    result = (T)locals.GetItem("delegate").AsManagedObject(typeof(T));
                    locals.Dispose();
                    return true;
                }
                catch
                {
                    // Do not throw or log the exception.
                    // Return false as an exception means that the conversion could not be made.
                }
                locals.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Convert a <see cref="PyObject"/> into a managed object
        /// </summary>
        /// <typeparam name="T">Target type of the resulting managed object</typeparam>
        /// <param name="pyObject">PyObject to be converted</param>
        /// <returns>Instance of type T</returns>
        public static T ConvertToDelegate<T>(this PyObject pyObject)
        {
            T result;
            if (pyObject.TryConvertToDelegate(out result))
            {
                return result;
            }
            else
            {
                throw new ArgumentException($"ConvertToDelegate cannot be used to convert a PyObject into {typeof(T)}.");
            }
        }

        /// <summary>
        /// Converts the numeric value of one or more enumerated constants to an equivalent enumerated string.
        /// </summary>
        /// <param name="value">Numeric value</param>
        /// <param name="pyObject">Python object that encapsulated a Enum Type</param>
        /// <returns>String that represents the enumerated object</returns>
        public static string GetEnumString(this int value, PyObject pyObject)
        {
            Type type;
            if (pyObject.TryConvert(out type))
            {
                return value.ToString().ConvertTo(type).ToString();
            }
            else
            {
                using (Py.GIL())
                {
                    throw new ArgumentException($"GetEnumString(): {pyObject.Repr()} is not a C# Type.");
                }
            }
        }

        /// <summary>
        /// Performs on-line batching of the specified enumerator, emitting chunks of the requested batch size
        /// </summary>
        /// <typeparam name="T">The enumerable item type</typeparam>
        /// <param name="enumerable">The enumerable to be batched</param>
        /// <param name="batchSize">The number of items per batch</param>
        /// <returns>An enumerable of lists</returns>
        public static IEnumerable<List<T>> BatchBy<T>(this IEnumerable<T> enumerable, int batchSize)
        {
            using (var enumerator = enumerable.GetEnumerator())
            {
                List<T> list = null;
                while (enumerator.MoveNext())
                {
                    if (list == null)
                    {
                        list = new List<T> {enumerator.Current};
                    }
                    else if (list.Count < batchSize)
                    {
                        list.Add(enumerator.Current);
                    }
                    else
                    {
                        yield return list;
                        list = new List<T> {enumerator.Current};
                    }
                }

                if (list?.Count > 0)
                {
                    yield return list;
                }
            }
        }

        /// <summary>
        /// Safely blocks until the specified task has completed executing
        /// </summary>
        /// <typeparam name="TResult">The task's result type</typeparam>
        /// <param name="task">The task to be awaited</param>
        /// <returns>The result of the task</returns>
        public static TResult SynchronouslyAwaitTaskResult<TResult>(this Task<TResult> task)
        {
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Safely blocks until the specified task has completed executing
        /// </summary>
        /// <param name="task">The task to be awaited</param>
        /// <returns>The result of the task</returns>
        public static void SynchronouslyAwaitTask(this Task task)
        {
            task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns a new string in which specified ending in the current instance is removed.
        /// </summary>
        /// <param name="s">original string value</param>
        /// <param name="ending">the string to be removed</param>
        /// <returns></returns>
        public static string RemoveFromEnd(this string s, string ending)
        {
            if (s.EndsWith(ending))
            {
                return s.Substring(0, s.Length - ending.Length);
            }
            else
            {
                return s;
            }
        }
    }
}
