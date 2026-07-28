using System.Collections.Generic;

namespace Tarinoi.Data
{
    /// <summary>
    /// The single source of truth for Tarinoi's two-layer document merge.
    /// </summary>
    /// <remarks>
    /// Authored content arrives on two layers. The <b>main</b> layer holds committed
    /// content; the <b>buffer</b> layer holds edits that have not been committed yet.
    /// The merge rules are:
    /// <list type="bullet">
    /// <item>An active buffer row overrides the main row.</item>
    /// <item>An <i>inactive</i> buffer row (tombstoned, archived or moved) suppresses
    /// the main row entirely — the buffer row acts as a deletion marker.</item>
    /// <item>With no buffer row at all, the main row shows if it is active.</item>
    /// </list>
    /// <para>
    /// The Godot plugin implements this twice — once as SQL in <c>TarinoiDB.active_filter()</c>
    /// and once in GDScript in <c>TarinoiRuntime._merge_layers()</c> — and the two
    /// drifted: the GDScript version checks only archived/moved and ignores tombstones.
    /// Both forms live here instead, sharing one definition of "active", so they cannot
    /// disagree.
    /// </para>
    /// </remarks>
    public static class LayerFilter
    {
        public const string MainLayer = "tarinoi:main-project-layer";
        public const string BufferLayer = "tarinoi:main-project-layer.buffer";

        /// <summary>
        /// Returns a SQL <c>WHERE</c> fragment (with no leading <c>AND</c>) selecting
        /// the documents that should be visible. Queries using it must alias the
        /// documents table as <c>d</c>.
        /// </summary>
        /// <param name="committedOnly">
        /// When true, ignore the buffer layer entirely and show only committed content.
        /// This is the "preview what players will see" mode.
        /// </param>
        public static string ActiveFilterSql(bool committedOnly)
        {
            if (committedOnly)
            {
                return $"d.layer_id = '{MainLayer}' "
                       + "AND d.is_tombstone = 0 AND d.is_archived = 0 AND d.is_moved = 0";
            }

            return $@"(
      (d.layer_id = '{BufferLayer}'
       AND d.is_tombstone = 0 AND d.is_archived = 0 AND d.is_moved = 0)
      OR
      (d.layer_id = '{MainLayer}'
       AND d.is_tombstone = 0 AND d.is_archived = 0 AND d.is_moved = 0
       AND NOT EXISTS (
         SELECT 1 FROM documents b
         WHERE b.document_id   = d.document_id
           AND b.collection_id = d.collection_id
           AND b.layer_id = '{BufferLayer}'
       ))
    )";
        }

        /// <summary>
        /// Applies the same rules in memory, for callers that select across all layers
        /// and merge afterwards (the runtime's global caches do this).
        /// </summary>
        /// <remarks>
        /// Input order is preserved: each surviving document appears at the position of
        /// its first row. Rows on an unrecognised layer are passed through untouched
        /// when active, so a future third layer degrades to "visible" rather than
        /// silently vanishing.
        /// </remarks>
        public static List<DocumentRow> Merge(IEnumerable<DocumentRow> rows, bool committedOnly)
        {
            var order = new List<(string DocumentId, string CollectionId)>();
            var main = new Dictionary<(string, string), DocumentRow>();
            var buffer = new Dictionary<(string, string), DocumentRow>();
            var other = new List<DocumentRow>();

            foreach (var row in rows)
            {
                var key = (row.DocumentId, row.CollectionId);

                switch (row.LayerId)
                {
                    case MainLayer:
                        if (!main.ContainsKey(key) && !buffer.ContainsKey(key))
                        {
                            order.Add(key);
                        }
                        main[key] = row;
                        break;
                    case BufferLayer:
                        if (!main.ContainsKey(key) && !buffer.ContainsKey(key))
                        {
                            order.Add(key);
                        }
                        buffer[key] = row;
                        break;
                    default:
                        if (row.IsActive)
                        {
                            other.Add(row);
                        }
                        break;
                }
            }

            var result = new List<DocumentRow>();
            foreach (var key in order)
            {
                if (!committedOnly && buffer.TryGetValue(key, out var bufferRow))
                {
                    // An inactive buffer row suppresses the main row rather than
                    // falling back to it.
                    if (bufferRow.IsActive)
                    {
                        result.Add(bufferRow);
                    }

                    continue;
                }

                if (main.TryGetValue(key, out var mainRow) && mainRow.IsActive)
                {
                    result.Add(mainRow);
                }
            }

            result.AddRange(other);
            return result;
        }
    }
}
