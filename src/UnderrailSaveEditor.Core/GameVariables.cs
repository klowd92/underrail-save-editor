using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// Global game state variables (quest flags, NPC states, ...): the save's Ouroboros.Common.Data.PropertyCollection.
    /// That class kept its real name, so its public API is used as is.
    /// </summary>
    public sealed class GameVariables
    {
        private readonly object _collection;
        private readonly Type _t;

        internal GameVariables(object propertyCollection)
        {
            _collection = propertyCollection;
            _t = propertyCollection.GetType();
        }

        public IReadOnlyList<KeyValuePair<string, object>> All
        {
            get
            {
                var props = (IEnumerable)_t.GetProperty("Properties").GetValue(_collection);
                return props.Cast<object>().Select(p => new KeyValuePair<string, object>(
                    (string)p.GetType().GetProperty("Name").GetValue(p),
                    p.GetType().GetProperty("Value").GetValue(p))).ToList();
            }
        }

        public bool Contains(string name) => (bool)_t.GetMethod("HasProperty").Invoke(_collection, new object[] { name });

        public object Get(string name) => _t.GetMethod("GetPropertyValue").Invoke(_collection, new object[] { name });

        public void Set(string name, object value) => _t.GetMethod("SetPropertyValue").Invoke(_collection, new object[] { name, value });

        public bool Remove(string name) => (bool)_t.GetMethod("RemoveProperty").Invoke(_collection, new object[] { name });

        /// <summary>
        /// Parses user text into a value. An existing variable keeps its type (an int stays an int);
        /// a new one is inferred as bool, int, double or string.
        /// </summary>
        public static object Parse(string text, Type existingType = null)
        {
            text = text?.Trim() ?? "";
            if (existingType != null && existingType != typeof(string))
            {
                if (existingType == typeof(bool))
                    return bool.Parse(text);
                if (existingType.IsEnum)
                    return Enum.Parse(existingType, text, true);
                if (existingType.IsPrimitive || existingType == typeof(decimal))
                    return Convert.ChangeType(text, existingType, CultureInfo.InvariantCulture);
                throw new NotSupportedException($"Variables of type {existingType.Name} can't be edited as text.");
            }
            if (existingType == typeof(string))
                return text;
            if (bool.TryParse(text, out var b))
                return b;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
            return text;
        }

        public static string Format(object value) => value switch
        {
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable e when !(value is string) => $"{e.GetType().Name.Split('`')[0]} ({e.Cast<object>().Count()} items)",
            _ => value.ToString(),
        };
    }
}
