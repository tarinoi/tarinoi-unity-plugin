using System.Runtime.CompilerServices;

// The editor assembly drives sync, codegen and snapshot export, which need access to
// internals the public runtime API deliberately doesn't expose.
[assembly: InternalsVisibleTo("Tarinoi.Editor")]

// Tests assert against internal helpers (parsers, SQL fragment builders) directly,
// rather than only through the public surface.
[assembly: InternalsVisibleTo("Tarinoi.Editor.Tests")]
