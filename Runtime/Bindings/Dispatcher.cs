using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tarinoi.Expressions;

namespace Tarinoi.Bindings
{
    /// <summary>
    /// Evaluates authored expressions against the game's bindings.
    /// </summary>
    /// <remarks>
    /// Nothing here throws. Every failure — an unbound collection, a missing function,
    /// an unknown list key — is logged and degrades to a false or null result. Authored
    /// content is data, and a mistake in it should show up as a dialogue that behaves
    /// oddly, not as a crashed game.
    /// <para>
    /// Parsed expressions are cached by their source text, including failures, so a
    /// malformed expression is reported once rather than on every evaluation.
    /// </para>
    /// </remarks>
    public sealed class Dispatcher
    {
        readonly BindingRegistry _registry;
        readonly Dictionary<string, ExprNode> _conditionCache = new Dictionary<string, ExprNode>();
        readonly Dictionary<string, CallNode> _callCache = new Dictionary<string, CallNode>();

        Dictionary<string, List<JObject>> _lists = new Dictionary<string, List<JObject>>();
        JObject _contextCard = new JObject();

        public Dispatcher(BindingRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Replaces the authored list data behind <c>Ls.*</c>. Called after configuration
        /// and after every sync, so lists always reflect current content.
        /// </summary>
        public void SetLists(Dictionary<string, List<JObject>> lists)
        {
            _lists = lists ?? new Dictionary<string, List<JObject>>();
        }

        /// <summary>Sets the card that <c>Card.*</c> resolves to.</summary>
        public void SetContextCard(JObject card)
        {
            _contextCard = card ?? new JObject();
        }

        // ---------------------------------------------------------------------
        // Public evaluation
        // ---------------------------------------------------------------------

        /// <summary>
        /// Evaluates a condition. An empty or unparseable expression is true — an
        /// author who wrote no condition, or a broken one, should not silently lose
        /// content.
        /// </summary>
        public bool EvalCondition(string expr)
        {
            if (string.IsNullOrEmpty(expr))
            {
                return true;
            }

            var ast = GetConditionAst(expr);
            return ast == null || ToBool(Eval(ast));
        }

        /// <summary>Evaluates a function call. Returns "" when empty or unparseable.</summary>
        public object EvalCall(string expr)
        {
            if (string.IsNullOrEmpty(expr))
            {
                return "";
            }

            var ast = GetCallAst(expr);
            return ast == null ? "" : Eval(ast);
        }

        /// <summary>
        /// Evaluates any expression and returns its raw value, without coercing to bool.
        /// </summary>
        public object EvalValue(string expr)
        {
            if (string.IsNullOrEmpty(expr))
            {
                return null;
            }

            var ast = GetConditionAst(expr);
            return ast == null ? null : Eval(ast);
        }

        /// <summary>
        /// Whether a call expression names a function that is actually bound and
        /// callable. Lets callers fall back gracefully instead of logging an error.
        /// </summary>
        public bool HasCall(string expr)
        {
            if (string.IsNullOrEmpty(expr))
            {
                return false;
            }

            var ast = GetCallAst(expr);
            if (ast == null)
            {
                return false;
            }

            var impl = _registry.GetFunctions(ast.Collection);
            return impl != null && impl.HasFunction(ast.Name);
        }

        // ---------------------------------------------------------------------
        // Caching
        // ---------------------------------------------------------------------

        ExprNode GetConditionAst(string expr)
        {
            if (_conditionCache.TryGetValue(expr, out var cached))
            {
                return cached;
            }

            var ast = ExpressionParser.ParseCondition(expr);
            _conditionCache[expr] = ast;
            return ast;
        }

        CallNode GetCallAst(string expr)
        {
            if (_callCache.TryGetValue(expr, out var cached))
            {
                return cached;
            }

            var ast = ExpressionParser.ParseCall(expr);
            _callCache[expr] = ast;
            return ast;
        }

        // ---------------------------------------------------------------------
        // Evaluation
        // ---------------------------------------------------------------------

        object Eval(ExprNode node)
        {
            switch (node)
            {
                case null:
                    return true;
                case BoolLiteral b:
                    return b.Value;
                case IntLiteral i:
                    return i.Value;
                case FloatLiteral f:
                    return f.Value;
                case StringLiteral s:
                    return s.Value;
                case NotNode n:
                    return !ToBool(Eval(n.Operand));
                case AndNode a:
                    // Short-circuits: the right side must not run, because authored
                    // functions have side effects.
                    return ToBool(Eval(a.Left)) && ToBool(Eval(a.Right));
                case OrNode o:
                    return ToBool(Eval(o.Left)) || ToBool(Eval(o.Right));
                case CallNode c:
                    return DispatchCall(c);
                case RefNode r:
                    return ResolveRef(r);
                case CardRefNode _:
                    return _contextCard;
                default:
                    TarinoiLog.Error($"Dispatcher: unknown expression node '{node.GetType().Name}'");
                    return false;
            }
        }

        object DispatchCall(CallNode node)
        {
            var impl = _registry.GetFunctions(node.Collection);
            if (impl == null)
            {
                TarinoiLog.Error($"Dispatcher: no bindings registered for function collection "
                                 + $"'{node.Collection}' (needed by {node})");
                return false;
            }

            if (!impl.HasFunction(node.Name))
            {
                TarinoiLog.Error($"Dispatcher: function '{node.Collection}.{node.Name}' is not "
                                 + "implemented. Regenerate your bindings if it was added recently.");
                return false;
            }

            var args = node.Args.Select(Eval).ToArray();

            if (TarinoiLog.Level <= TarinoiLogLevel.Debug)
            {
                TarinoiLog.Debug($"→ Fn.{node.Collection}.{node.Name}("
                                 + string.Join(", ", args.Select(a => a?.ToString() ?? "null")) + ")");
            }

            if (!impl.TryInvoke(node.Name, args, out var result))
            {
                TarinoiLog.Error($"Dispatcher: could not call '{node.Collection}.{node.Name}'");
                return false;
            }

            TarinoiLog.Debug($"  ← {result ?? "null"}");
            return result;
        }

        object ResolveRef(RefNode node)
        {
            switch (node.Kind)
            {
                case RefKind.Variable:
                {
                    var impl = _registry.GetVariables(node.Collection);
                    if (impl == null)
                    {
                        TarinoiLog.Error($"Dispatcher: no bindings registered for variable collection "
                                         + $"'{node.Collection}' (needed by {node})");
                        return null;
                    }

                    // Deliberately not read here: functions receive the reference so they
                    // can write to it. See VarRef.
                    return new VarRef(impl, node.Collection, node.Name);
                }

                case RefKind.Entity:
                {
                    var impl = _registry.GetEntities(node.Collection);
                    if (impl == null)
                    {
                        TarinoiLog.Error($"Dispatcher: no bindings registered for entity collection "
                                         + $"'{node.Collection}' (needed by {node})");
                        return null;
                    }

                    return impl.GetEntity(node.Name);
                }

                default:
                    return ResolveListOption(node);
            }
        }

        object ResolveListOption(RefNode node)
        {
            var key = node.Collection + "/" + node.Name;
            if (!_lists.TryGetValue(key, out var options))
            {
                TarinoiLog.Warn($"Dispatcher: no list '{node.Collection}.{node.Name}' in the synced "
                                + "content — has it been synced since the list was added?");
                return null;
            }

            foreach (var option in options)
            {
                if ((string)option["key"] == node.Key)
                {
                    // Newer content uses option_value; older content used value.
                    var value = option["option_value"] ?? option["value"];
                    return value == null || value.Type == JTokenType.Null ? null : ((JValue)value).Value;
                }
            }

            TarinoiLog.Warn($"Dispatcher: list '{node.Collection}.{node.Name}' has no option "
                            + $"'{node.Key}'");
            return null;
        }

        // ---------------------------------------------------------------------
        // Coercion
        // ---------------------------------------------------------------------

        /// <summary>
        /// Coerces an evaluated value to a boolean.
        /// </summary>
        /// <remarks>
        /// A <see cref="VarRef"/> is read here rather than treated as a plain object.
        /// This is a deliberate departure from the Godot plugin, where a bare
        /// <c>Var.collection.flag</c> used as a condition is always true because the
        /// unresolved reference object is itself truthy — which makes such conditions
        /// silently useless. Reading it is what an author means.
        /// </remarks>
        internal static bool ToBool(object value)
        {
            value = VarRef.Resolve(value);

            switch (value)
            {
                case null:
                    return false;
                case bool b:
                    return b;
                case string s:
                    return s.Length > 0;
                case long l:
                    return l != 0;
                case int i:
                    return i != 0;
                case double d:
                    return d != 0;
                case float f:
                    return f != 0;
                case decimal m:
                    return m != 0;
                case JValue jv:
                    return ToBool(jv.Value);
                case JToken jt:
                    return jt.Type != JTokenType.Null && jt.HasValues;
                default:
                    return true;
            }
        }
    }
}
