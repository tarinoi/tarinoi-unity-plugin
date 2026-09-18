// Tarinoi core functions 0.0.1 — reference implementation.
// Scaffolded by Tarinoi from the synced content of project 'fixture'.
//
// This file is yours: edit it freely, regenerating never overwrites it. Delete
// it to get a fresh copy the next time you regenerate bindings.
//
// These are the Fn.tarinoi.* functions in-app playback evaluates for real.
// Keep the same semantics — in particular the defaults for unset variables — so
// that what an author verified in playback holds in the game.

using System.Globalization;
using Tarinoi;
using Tarinoi.Bindings;
using Tarinoi.Tests.Fixtures;
using UnityEngine.Scripting;

namespace Tarinoi.Tests.Fixtures
{
    /// <summary>Tarinoi's core functions, callable as <c>Fn.tarinoi.*</c>.</summary>
    /// <remarks>Bind an instance under <c>"tarinoi"</c>: <c>Registry.BindFunctions("tarinoi", new TarinoiCoreFunctions())</c>. The quickstart component does this on its own.</remarks>
    [Preserve]
    public class TarinoiCoreFunctions : TarinoiFunctions
    {
        /// <summary>The version of the core set this file was scaffolded from.</summary>
        public const string ScaffoldVersion = "0.0.1";

        /// <summary>Set a flag.</summary>
        public override void SetFlag(object flagRef) => Write(flagRef, true);

        /// <summary>Clear a flag.</summary>
        public override void ClearFlag(object flagRef) => Write(flagRef, false);

        /// <summary>Toggle a flag: clear if set, set if unset.</summary>
        public override void ToggleFlag(object flagRef) => Write(flagRef, !Flag(flagRef));

        /// <summary>Set a counter to a value, which must be a number.</summary>
        public override void SetCounter(object counterRef, object value) => Write(counterRef, Number(value));

        /// <summary>Increment a counter by delta, which must be a number. Use negative numbers to decrement. An unset counter is treated as a 0.</summary>
        public override void IncrementCounter(object counterRef, object delta) => Write(counterRef, Number(counterRef) + Number(delta));

        /// <summary>Set a text variable to a value, which must be a string.</summary>
        public override void SetText(object textRef, object value) => Write(textRef, Text(value));

        /// <summary>Returns true if the flag is set. An unset flag is treated as clear. Use the NOT operator in conditions to negate.</summary>
        public override bool FlagIsSet(object flagRef) => Flag(flagRef);

        /// <summary>Returns true if a and b equal each other. Both must be numbers. An unset counter is treated as a 0. Use the NOT operator in conditions to negate.</summary>
        public override bool NumberEquals(object a, object b) => Number(a) == Number(b);

        /// <summary>Returns true if a >= b. Both must be numbers. An unset counter is treated as a 0.</summary>
        public override bool NumberAtLeast(object a, object b) => Number(a) >= Number(b);

        /// <summary>Returns true if a > b. Both must be numbers. An unset counter is treated as a 0.</summary>
        public override bool NumberGreaterThan(object a, object b) => Number(a) > Number(b);

        /// <summary>Returns true if a <= b. Both must be numbers. An unset counter is treated as a 0.</summary>
        public override bool NumberAtMost(object a, object b) => Number(a) <= Number(b);

        /// <summary>Returns true if a < b. Both must be numbers. An unset counter is treated as a 0.</summary>
        public override bool NumberLessThan(object a, object b) => Number(a) < Number(b);

        /// <summary>Returns true if a and b equal each other. Both must be strings. An unset text variable is treated as empty. Use the NOT operator in conditions to negate.</summary>
        public override bool StringEquals(object a, object b) => Text(a) == Text(b);

        // -------------------------------------------------------------------------
        // Helpers. The dispatcher passes a Var.* argument as a VarRef so the function
        // can write through it, and anything else as a plain value. Unset variables
        // read as false / 0 / "", matching in-app playback. A value of the wrong type
        // is an authoring error: it is logged, and the default is used instead.
        // -------------------------------------------------------------------------

        /// <summary>Writes through a Var.* reference.</summary>
        static void Write(object reference, object value)
        {
            if (reference is VarRef variable)
            {
                variable.Value = value;
                return;
            }

            TarinoiLog.Error($"Core functions: expected a Var.* reference, got {Describe(reference)}.");
        }

        /// <summary>Reads a boolean: a flag, or a literal.</summary>
        static bool Flag(object value)
        {
            switch (ValueConvert.Unwrap(value))
            {
                case null: return false;
                case bool flag: return flag;
            }

            TarinoiLog.Error($"Core functions: expected a boolean, got {Describe(value)}.");
            return false;
        }

        /// <summary>Reads a number: a counter, a list item, or a literal.</summary>
        static double Number(object value)
        {
            switch (ValueConvert.Unwrap(value))
            {
                case null: return 0d;
                case string _:
                case bool _:
                    break;
                case System.IConvertible number:
                    return number.ToDouble(CultureInfo.InvariantCulture);
            }

            TarinoiLog.Error($"Core functions: expected a number, got {Describe(value)}.");
            return 0d;
        }

        /// <summary>Reads a string: a text variable, a list item, or a literal.</summary>
        static string Text(object value)
        {
            switch (ValueConvert.Unwrap(value))
            {
                case null: return "";
                case string text: return text;
            }

            TarinoiLog.Error($"Core functions: expected a string, got {Describe(value)}.");
            return "";
        }

        static string Describe(object value) =>
            value is VarRef variable
                ? $"{variable} = {variable.Value ?? "unset"}"
                : $"{value ?? "null"} ({value?.GetType().Name ?? "null"})";
    }
}
