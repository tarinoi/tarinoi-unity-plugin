using System;
using System.Collections.Generic;
using System.Reflection;

namespace Tarinoi.Bindings
{
    /// <summary>
    /// Adapts any plain object into <see cref="ITarinoiFunctions"/> by looking its
    /// methods up reflectively.
    /// </summary>
    /// <remarks>
    /// This exists for convenience: you can bind a class you wrote by hand, without
    /// implementing an interface or running codegen.
    /// <para>
    /// <b>Prefer generated bindings for anything you ship.</b> Managed code stripping
    /// and IL2CPP can remove methods that are only ever called reflectively, so a
    /// binding that works in the editor can silently fail in a built player. Generated
    /// classes implement <see cref="ITarinoiFunctions.TryInvoke"/> as a direct
    /// <c>switch</c> and have no such problem.
    /// </para>
    /// </remarks>
    public sealed class ReflectionFunctions : ITarinoiFunctions
    {
        readonly object _target;
        readonly Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();

        public ReflectionFunctions(object target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));

            foreach (var method in _target.GetType()
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                // Overloads are not part of the Tarinoi binding model: an authored
                // function name maps to exactly one implementation.
                if (_methods.ContainsKey(method.Name))
                {
                    TarinoiLog.Warn(
                        $"Bindings: '{_target.GetType().Name}.{method.Name}' is overloaded; "
                        + "Tarinoi will use the first overload found. Give the functions distinct names.");
                    continue;
                }

                _methods[method.Name] = method;
            }
        }

        public bool HasFunction(string name) =>
            name != null && _methods.ContainsKey(name);

        public bool TryInvoke(string name, object[] args, out object result)
        {
            result = null;
            if (name == null || !_methods.TryGetValue(name, out var method))
            {
                return false;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != (args?.Length ?? 0))
            {
                TarinoiLog.Error(
                    $"Bindings: '{method.Name}' takes {parameters.Length} argument(s) but the "
                    + $"authored call passed {args?.Length ?? 0}. Regenerate your bindings.");
                return true;
            }

            try
            {
                result = method.Invoke(_target, args);
            }
            catch (TargetInvocationException e)
            {
                // Surface the game code's own exception, not the reflection wrapper.
                TarinoiLog.Error($"Bindings: '{method.Name}' threw: {e.InnerException?.Message ?? e.Message}");
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Bindings: could not call '{method.Name}': {e.Message}");
            }

            return true;
        }
    }
}
