namespace Tarinoi.Bindings
{
    /// <summary>
    /// A reference to a game variable that has been located but not read.
    /// </summary>
    /// <remarks>
    /// When an author passes <c>Var.collection.name</c> as a function argument, the
    /// function receives one of these rather than the variable's value. That is what
    /// lets a function write back:
    /// <code>
    /// public void SetFlag(object flag) => ((VarRef)flag).Value = true;
    ///
    /// public bool CheckAndClear(object flag)
    /// {
    ///     var reference = (VarRef)flag;
    ///     var was = VarRef.Resolve(reference);
    ///     reference.Value = false;
    ///     return (bool)was;
    /// }
    /// </code>
    /// A function that only needs to read should call <see cref="Resolve"/>, which
    /// passes plain values through unchanged — so it works whether the author wrote a
    /// variable reference or a literal.
    /// </remarks>
    public sealed class VarRef
    {
        readonly ITarinoiVariables _impl;

        public string Collection { get; }
        public string Name { get; }

        public VarRef(ITarinoiVariables impl, string collection, string name)
        {
            _impl = impl;
            Collection = collection;
            Name = name;
        }

        /// <summary>Reads or writes the underlying variable.</summary>
        public object Value
        {
            get => _impl.GetVariable(Name);
            set => _impl.SetVariable(Name, value);
        }

        /// <summary>
        /// Unwraps a <see cref="VarRef"/> to its current value, passing anything else
        /// through unchanged.
        /// </summary>
        public static object Resolve(object value) =>
            value is VarRef reference ? reference.Value : value;

        public override string ToString() => $"Var.{Collection}.{Name}";
    }
}
