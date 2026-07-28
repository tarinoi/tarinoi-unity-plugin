using SQLite;

namespace Tarinoi.Data
{
    /// <summary>
    /// One row of the <c>documents</c> table, as stored — before any layer merge.
    /// </summary>
    /// <remarks>
    /// The primary key is (<see cref="DocumentId"/>, <see cref="CollectionId"/>,
    /// <see cref="LayerId"/>), so the same logical document exists once per layer.
    /// <see cref="Payload"/> stays as the raw JSON text the service sent; callers parse
    /// it themselves, because document shapes vary by <see cref="DocumentType"/>.
    /// </remarks>
    public class DocumentRow
    {
        [Column("document_id")] public string DocumentId { get; set; }
        [Column("collection_id")] public string CollectionId { get; set; }
        [Column("document_type")] public string DocumentType { get; set; }
        [Column("layer_id")] public string LayerId { get; set; }
        [Column("namespace")] public string Namespace { get; set; }
        [Column("identifier")] public string Identifier { get; set; }
        [Column("update_key")] public long UpdateKey { get; set; }
        [Column("is_tombstone")] public int IsTombstone { get; set; }
        [Column("is_archived")] public int IsArchived { get; set; }
        [Column("is_moved")] public int IsMoved { get; set; }
        [Column("payload")] public string Payload { get; set; }

        /// <summary>
        /// Whether this row represents live content. The three flags are separate
        /// because the service distinguishes deletion, archival and relocation, but
        /// none of them should ever reach a player.
        /// </summary>
        [Ignore]
        public bool IsActive => IsTombstone == 0 && IsArchived == 0 && IsMoved == 0;
    }

    /// <summary>One row of the <c>collections</c> table.</summary>
    public class CollectionRow
    {
        [Column("collection_id")] public string CollectionId { get; set; }
        [Column("collection_name")] public string CollectionName { get; set; }
        [Column("collection_type")] public string CollectionType { get; set; }
        [Column("payload")] public string Payload { get; set; }
    }

    /// <summary>A dialogue entry point, as listed by the start-card picker.</summary>
    public class StartCardRow
    {
        [Column("document_id")] public string DocumentId { get; set; }
        [Column("collection_id")] public string CollectionId { get; set; }
        [Column("label")] public string Label { get; set; }
    }
}
