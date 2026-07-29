using Tarinoi;
using Tarinoi.Bindings;

namespace TarinoiSample
{
    /// <summary>
    /// The game state an author reads and writes as <c>Var.global.*</c>.
    /// </summary>
    /// <remarks>
    /// Written by hand here so the sample runs before you have synced anything. Once you
    /// have real content, run <b>Tools → Tarinoi → Regenerate Bindings</b> and derive from
    /// the generated <c>GlobalVariables</c> instead — it will have a typed field per
    /// variable your authors declared, and stay in step with them.
    /// </remarks>
    public class MyVariables : ITarinoiVariables
    {
        public bool MetTheNarrator;
        public double Courage = 1;

        public object GetVariable(string name)
        {
            switch (name)
            {
                case "met_the_narrator": return MetTheNarrator;
                case "courage": return Courage;
                default:
                    TarinoiLog.Warn($"MyVariables has no '{name}'.");
                    return null;
            }
        }

        public void SetVariable(string name, object value)
        {
            switch (name)
            {
                case "met_the_narrator":
                    MetTheNarrator = ValueConvert.ToBool(value);
                    return;
                case "courage":
                    Courage = ValueConvert.ToDouble(value);
                    return;
                default:
                    TarinoiLog.Warn($"MyVariables has no '{name}'.");
                    return;
            }
        }
    }

    /// <summary>
    /// The functions an author calls as <c>Fn.global.*</c>.
    /// </summary>
    /// <remarks>
    /// Bound as a plain object, so Tarinoi finds these methods by name. That is convenient
    /// while prototyping; for a shipped build prefer the generated base class, whose
    /// dispatch survives IL2CPP code stripping.
    /// <para>
    /// Note <see cref="SetFlag"/>: a <c>Var.*</c> argument arrives as a <see cref="VarRef"/>
    /// rather than a value, which is what lets a function write back to it.
    /// </para>
    /// </remarks>
    public class MyFunctions
    {
        readonly MyVariables _variables;

        public MyFunctions(MyVariables variables) => _variables = variables;

        /// <summary>Reads a flag: <c>Fn.global.CheckFlag(Var.global.met_the_narrator)</c>.</summary>
        public bool CheckFlag(object flag) => ValueConvert.ToBool(VarRef.Resolve(flag));

        /// <summary>Writes a flag: <c>Fn.global.SetFlag(Var.global.met_the_narrator)</c>.</summary>
        public void SetFlag(object flag)
        {
            if (flag is VarRef reference)
            {
                reference.Value = true;
            }
        }

        /// <summary>Adds to a number: <c>Fn.global.AdjustCounter(Var.global.courage, 1)</c>.</summary>
        public void AdjustCounter(object counter, object delta)
        {
            if (counter is VarRef reference)
            {
                reference.Value = ValueConvert.ToDouble(reference.Value)
                                  + ValueConvert.ToDouble(delta);
            }
        }

        /// <summary>
        /// Picks an output pin, and tells the player why.
        /// </summary>
        /// <remarks>
        /// Used as a card's output selector. The posted line appears as an interstitial the
        /// player advances past before the chosen branch begins.
        /// </remarks>
        public string CheckSkill(object _)
        {
            var succeeded = _variables.Courage >= 1;
            TarinoiRuntime.Instance.PostSystemLine(
                succeeded ? "Your nerve holds." : "Your nerve fails you.");

            return succeeded ? "success" : "failure";
        }
    }
}
