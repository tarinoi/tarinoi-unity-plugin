using System.Collections.Generic;
using System.Linq;

namespace Tarinoi.Expressions
{
    /// <summary>
    /// A parsed Tarinoi expression.
    /// </summary>
    /// <remarks>
    /// The Godot plugin represents these as untyped dictionaries keyed by a
    /// <c>"type"</c> string. Here they are real types, so the evaluator dispatches on
    /// the node class and the compiler catches a malformed tree rather than a
    /// null-key lookup at runtime.
    /// </remarks>
    public abstract class ExprNode
    {
    }

    public sealed class BoolLiteral : ExprNode
    {
        public readonly bool Value;
        public BoolLiteral(bool value) => Value = value;
        public override string ToString() => Value ? "true" : "false";
    }

    public sealed class IntLiteral : ExprNode
    {
        public readonly long Value;
        public IntLiteral(long value) => Value = value;
        public override string ToString() => Value.ToString();
    }

    public sealed class FloatLiteral : ExprNode
    {
        public readonly double Value;
        public FloatLiteral(double value) => Value = value;
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class StringLiteral : ExprNode
    {
        public readonly string Value;
        public StringLiteral(string value) => Value = value;
        public override string ToString() => $"\"{Value}\"";
    }

    public sealed class NotNode : ExprNode
    {
        public readonly ExprNode Operand;
        public NotNode(ExprNode operand) => Operand = operand;
        public override string ToString() => $"!{Operand}";
    }

    public sealed class AndNode : ExprNode
    {
        public readonly ExprNode Left;
        public readonly ExprNode Right;
        public AndNode(ExprNode left, ExprNode right)
        {
            Left = left;
            Right = right;
        }

        public override string ToString() => $"({Left} && {Right})";
    }

    public sealed class OrNode : ExprNode
    {
        public readonly ExprNode Left;
        public readonly ExprNode Right;
        public OrNode(ExprNode left, ExprNode right)
        {
            Left = left;
            Right = right;
        }

        public override string ToString() => $"({Left} || {Right})";
    }

    /// <summary>A call to a game-provided function: <c>Fn.collection.Name(args)</c>.</summary>
    public sealed class CallNode : ExprNode
    {
        public readonly string Collection;
        public readonly string Name;
        public readonly IReadOnlyList<ExprNode> Args;

        public CallNode(string collection, string name, IReadOnlyList<ExprNode> args)
        {
            Collection = collection;
            Name = name;
            Args = args;
        }

        public override string ToString() =>
            $"Fn.{Collection}.{Name}({string.Join(", ", Args.Select(a => a.ToString()))})";
    }

    public enum RefKind
    {
        /// <summary>A game variable: <c>Var.collection.name</c>.</summary>
        Variable,

        /// <summary>A game entity: <c>Ent.collection.name</c>.</summary>
        Entity,

        /// <summary>An authored list option: <c>Ls.collection.list.key</c>.</summary>
        List,
    }

    /// <summary>A reference to game or authored data.</summary>
    public sealed class RefNode : ExprNode
    {
        public readonly RefKind Kind;
        public readonly string Collection;
        public readonly string Name;

        /// <summary>Only meaningful for <see cref="RefKind.List"/>.</summary>
        public readonly string Key;

        public RefNode(RefKind kind, string collection, string name, string key = "")
        {
            Kind = kind;
            Collection = collection;
            Name = name;
            Key = key;
        }

        public string Prefix
        {
            get
            {
                switch (Kind)
                {
                    case RefKind.Variable: return "Var";
                    case RefKind.Entity: return "Ent";
                    default: return "Ls";
                }
            }
        }

        public override string ToString() =>
            Kind == RefKind.List
                ? $"Ls.{Collection}.{Name}.{Key}"
                : $"{Prefix}.{Collection}.{Name}";
    }

    /// <summary>A reference to the card currently being processed: <c>Card.Name</c>.</summary>
    public sealed class CardRefNode : ExprNode
    {
        public readonly string Name;
        public CardRefNode(string name) => Name = name;
        public override string ToString() => $"Card.{Name}";
    }
}
