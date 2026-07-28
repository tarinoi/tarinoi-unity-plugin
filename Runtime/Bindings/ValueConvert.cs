using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Tarinoi.Bindings
{
    /// <summary>
    /// Converts values arriving from authored expressions into the types your bindings
    /// declare.
    /// </summary>
    /// <remarks>
    /// Values can reach a binding as a boxed <c>long</c> or <c>double</c> from a literal,
    /// a <see cref="JValue"/> from a card payload, or a <see cref="VarRef"/> the author
    /// passed through. Generated bindings route every assignment through here so a type
    /// mismatch degrades to a default rather than throwing inside game code.
    /// </remarks>
    public static class ValueConvert
    {
        /// <summary>Unwraps references and JSON values down to a plain CLR value.</summary>
        public static object Unwrap(object value)
        {
            value = VarRef.Resolve(value);
            return value is JValue jv ? jv.Value : value;
        }

        public static bool ToBool(object value, bool fallback = false)
        {
            value = Unwrap(value);

            switch (value)
            {
                case null: return fallback;
                case bool b: return b;
                case string s: return s.Length > 0 && s != "false" && s != "0";
                case IConvertible _:
                    try
                    {
                        return Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0d;
                    }
                    catch (Exception)
                    {
                        return fallback;
                    }
                default: return true;
            }
        }

        public static double ToDouble(object value, double fallback = 0d)
        {
            value = Unwrap(value);

            if (value == null)
            {
                return fallback;
            }

            if (value is bool b)
            {
                return b ? 1d : 0d;
            }

            if (value is string s)
            {
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : fallback;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public static string ToText(object value, string fallback = "")
        {
            value = Unwrap(value);

            if (value == null)
            {
                return fallback;
            }

            // Invariant formatting: a binding's value must not change with the player's
            // locale.
            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }
    }
}
