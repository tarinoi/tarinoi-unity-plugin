using Tarinoi;
using Tarinoi.Bindings;

namespace Tarinoi.Tests.Fixtures
{
    /// <summary>
    /// What <c>CodeEmitter.Functions</c> emits for the <c>tarinoi</c> collection, in the
    /// fixture namespace: the base the golden scaffold derives from, so the scaffold
    /// compiles into the test assembly and its behaviour can be tested for real.
    /// </summary>
    public abstract partial class TarinoiFunctions : ITarinoiFunctions
    {
        public virtual void ClearFlag(object flagRef) => Unimplemented("ClearFlag");
        public virtual bool FlagIsSet(object flagRef) { Unimplemented("FlagIsSet"); return false; }
        public virtual void IncrementCounter(object counterRef, object delta) => Unimplemented("IncrementCounter");
        public virtual bool NumberAtLeast(object a, object b) { Unimplemented("NumberAtLeast"); return false; }
        public virtual bool NumberAtMost(object a, object b) { Unimplemented("NumberAtMost"); return false; }
        public virtual bool NumberEquals(object a, object b) { Unimplemented("NumberEquals"); return false; }
        public virtual bool NumberGreaterThan(object a, object b) { Unimplemented("NumberGreaterThan"); return false; }
        public virtual bool NumberLessThan(object a, object b) { Unimplemented("NumberLessThan"); return false; }
        public virtual void SetCounter(object counterRef, object value) => Unimplemented("SetCounter");
        public virtual void SetFlag(object flagRef) => Unimplemented("SetFlag");
        public virtual void SetText(object textRef, object value) => Unimplemented("SetText");
        public virtual bool StringEquals(object a, object b) { Unimplemented("StringEquals"); return false; }
        public virtual void ToggleFlag(object flagRef) => Unimplemented("ToggleFlag");

        static void Unimplemented(string name) =>
            TarinoiLog.Error($"TarinoiFunctions.{name} is not implemented. Override it in your bindings class.");

        public bool HasFunction(string name)
        {
            switch (name)
            {
                case "ClearFlag": case "FlagIsSet": case "IncrementCounter": case "NumberAtLeast":
                case "NumberAtMost": case "NumberEquals": case "NumberGreaterThan": case "NumberLessThan":
                case "SetCounter": case "SetFlag": case "SetText": case "StringEquals": case "ToggleFlag":
                    return true;
                default:
                    return false;
            }
        }

        public bool TryInvoke(string name, object[] args, out object result)
        {
            result = null;
            switch (name)
            {
                case "ClearFlag": ClearFlag(args[0]); return true;
                case "FlagIsSet": result = FlagIsSet(args[0]); return true;
                case "IncrementCounter": IncrementCounter(args[0], args[1]); return true;
                case "NumberAtLeast": result = NumberAtLeast(args[0], args[1]); return true;
                case "NumberAtMost": result = NumberAtMost(args[0], args[1]); return true;
                case "NumberEquals": result = NumberEquals(args[0], args[1]); return true;
                case "NumberGreaterThan": result = NumberGreaterThan(args[0], args[1]); return true;
                case "NumberLessThan": result = NumberLessThan(args[0], args[1]); return true;
                case "SetCounter": SetCounter(args[0], args[1]); return true;
                case "SetFlag": SetFlag(args[0]); return true;
                case "SetText": SetText(args[0], args[1]); return true;
                case "StringEquals": result = StringEquals(args[0], args[1]); return true;
                case "ToggleFlag": ToggleFlag(args[0]); return true;
                default: return false;
            }
        }
    }

    /// <summary>A generated variables class, as far as default binding is concerned.</summary>
    public partial class GlobalVariables : ITarinoiVariables
    {
        public const string Collection = "global";

        public bool Met;

        public object GetVariable(string name) => name == "met" ? Met : (object)null;
        public void SetVariable(string name, object value) { if (name == "met") Met = ValueConvert.ToBool(value); }
    }
}
