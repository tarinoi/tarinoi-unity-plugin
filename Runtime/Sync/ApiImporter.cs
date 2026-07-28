using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tarinoi.Data;

namespace Tarinoi.Sync
{
    /// <summary>
    /// Pulls authored content from the Tarinoi documents API into the local database.
    /// </summary>
    /// <remarks>
    /// The endpoint returns NDJSON pages plus a cursor. The cursor is an
    /// <c>update_key</c> watermark: the server sends documents newer than it, so
    /// storing it after each page makes the next sync incremental, and an interrupted
    /// sync resumes rather than restarting.
    /// <para>
    /// <b>Threading:</b> the caller supplies a <see cref="TarinoiDb"/> that this importer
    /// owns for the duration. Give it a connection of its own rather than the runtime's —
    /// a SQLite connection must not be shared across threads, and WAL mode is what lets
    /// this write while the runtime reads. Progress is reported through
    /// <see cref="IProgress{T}"/> so the caller decides how to get back to the main thread.
    /// </para>
    /// </remarks>
    public sealed class ApiImporter
    {
        /// <summary>Guards against a malformed server response paginating forever.</summary>
        const int MaxPages = 10000;

        readonly HttpMessageHandler _handler;
        readonly DataVersion _versionCheck = new DataVersion();

        /// <summary>
        /// </summary>
        /// <param name="handler">
        /// Transport override. Tests inject a fake handler to exercise pagination,
        /// error handling and upsert semantics without a network or an API key.
        /// </param>
        public ApiImporter(HttpMessageHandler handler = null)
        {
            _handler = handler;
        }

        /// <summary>
        /// Runs a full or incremental sync. Never throws: failures come back as
        /// <see cref="SyncResult.Error"/> so callers can surface them to the user.
        /// </summary>
        /// <param name="apiPath">The project's documents endpoint, ending in <c>/documents</c>.</param>
        /// <param name="apiKey">The API token, as a bearer credential.</param>
        /// <param name="db">An open database, exclusively for this sync.</param>
        public async Task<SyncResult> SyncAsync(
            string apiPath,
            string apiKey,
            TarinoiDb db,
            IProgress<SyncProgress> progress = null,
            bool skipTlsVerify = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return SyncResult.Fail(
                    "No API token saved — use Tools > Tarinoi > Set API token…");
            }

            if (string.IsNullOrWhiteSpace(apiPath))
            {
                return SyncResult.Fail(
                    "No API path set — check Project Settings > Tarinoi.");
            }

            if (db == null || !db.IsOpen)
            {
                return SyncResult.Fail("Sync needs an open database.");
            }

            if (!Uri.TryCreate(apiPath.Trim(), UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                return SyncResult.Fail($"Cannot parse the API path as a URL: {apiPath}");
            }

            var projectId = TarinoiSettings.ProjectIdFromApiPath(apiPath);
            if (string.IsNullOrEmpty(projectId))
            {
                return SyncResult.Fail($"Cannot derive a project id from the API path: {apiPath}");
            }

            db.WriteMeta(TarinoiDb.ProjectIdKey, projectId);
            db.WriteMeta(TarinoiDb.ApiPathKey, apiPath);

            var cursor = db.ReadMeta(TarinoiDb.ApiSyncCursorKey);
            progress?.Report(string.IsNullOrEmpty(cursor)
                ? new SyncProgress("Starting full sync…", 0f)
                : new SyncProgress($"Fetching changes since {cursor}…", 0.1f));

            try
            {
                // ConfigureAwait(false) throughout this class is load-bearing, not
                // stylistic. Unity installs a SynchronizationContext that posts
                // continuations to the main thread; if a caller blocks the main thread
                // waiting on this task, those continuations can never run and the
                // editor deadlocks. Library code must not capture the caller's context.
                return await RunAsync(baseUri, apiKey, cursor, db, progress, skipTlsVerify, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SyncResult.Fail("Sync cancelled.");
            }
            catch (Exception e)
            {
                return SyncResult.Fail($"Sync failed: {e.Message}");
            }
        }

        async Task<SyncResult> RunAsync(
            Uri baseUri, string apiKey, string cursor, TarinoiDb db,
            IProgress<SyncProgress> progress, bool skipTlsVerify, CancellationToken ct)
        {
            var stats = new SyncStats();
            var cursorAdvanced = false;
            var page = 0;

            using (var client = CreateClient(skipTlsVerify))
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    if (page >= MaxPages)
                    {
                        stats.Warnings.Add($"Stopped after {MaxPages} pages — the cursor never cleared.");
                        break;
                    }

                    progress?.Report(new SyncProgress(
                        $"Fetching page {page}…", Mathf01(0.1f + page * 0.05f)));

                    var requestUri = string.IsNullOrEmpty(cursor)
                        ? baseUri
                        : AppendCursor(baseUri, cursor);

                    NdjsonPage parsed;
                    using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

                        using (var response = await client.SendAsync(
                                   request, HttpCompletionOption.ResponseHeadersRead, ct)
                                   .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                return SyncResult.Fail(
                                    $"{HttpErrorHint(response.StatusCode)} (HTTP {(int)response.StatusCode} for {requestUri.PathAndQuery})");
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync()
                                       .ConfigureAwait(false))
                            {
                                parsed = await NdjsonReader.ParseAsync(stream, ct)
                                    .ConfigureAwait(false);
                            }
                        }
                    }

                    // Version-check the whole page before writing any of it. Bailing out
                    // mid-transaction would work too, but this way an incompatible page
                    // is rejected without touching the database at all — and the cursor
                    // stays put, so an updated client resumes from the same point.
                    foreach (var doc in parsed.Documents)
                    {
                        var versionError = _versionCheck.Check((string)doc["data_version"]);
                        if (!string.IsNullOrEmpty(versionError))
                        {
                            return SyncResult.Fail(versionError);
                        }
                    }

                    // One transaction per page: a few hundred individual inserts would
                    // otherwise each pay their own fsync.
                    db.RunInTransaction(() =>
                    {
                        foreach (var doc in parsed.Documents)
                        {
                            UpsertDocument(doc, db, stats);
                        }
                    });

                    if (parsed.Cursor == null)
                    {
                        break;
                    }

                    cursor = parsed.Cursor;
                    cursorAdvanced = true;
                    db.WriteMeta(TarinoiDb.ApiSyncCursorKey, cursor);
                    page++;
                }
            }

            // When the server sends no explicit cursor, derive one from the highest
            // update_key we hold. This covers both a full sync and a single-page
            // incremental response, and is what makes the *next* sync incremental.
            if (!cursorAdvanced)
            {
                var max = db.QueryScalars<long?>("SELECT MAX(update_key) FROM documents");
                if (max.Count > 0 && max[0].HasValue)
                {
                    db.WriteMeta(TarinoiDb.ApiSyncCursorKey, max[0].Value.ToString());
                }
            }

            RebuildCollections(db, stats);
            progress?.Report(new SyncProgress("Sync complete.", 1f));
            return SyncResult.Ok(stats);
        }

        // -------------------------------------------------------------------------
        // Upsert
        // -------------------------------------------------------------------------

        /// <summary>
        /// Applies one document from the feed.
        /// </summary>
        /// <remarks>
        /// Three server states, three different local outcomes:
        /// <list type="bullet">
        /// <item><b>Tombstoned</b> — a hard delete. The row for that layer goes, and if
        /// the document was a collection manifest its collections entry goes too.</item>
        /// <item><b>Archived or moved</b> — stored <i>with the flags intact</i>. On the
        /// buffer layer these rows are load-bearing: they suppress the committed
        /// version (see <see cref="LayerFilter"/>). Deleting them would wrongly resurrect it.</item>
        /// <item><b>Active</b> — a plain upsert.</item>
        /// </list>
        /// </remarks>
        internal void UpsertDocument(JObject doc, TarinoiDb db, SyncStats stats)
        {
            var documentId = (string)doc["document_id"] ?? "";
            var collectionId = (string)doc["collection_id"] ?? "";
            var layerId = (string)doc["layer_id"] ?? "";

            if (documentId.Length == 0 || collectionId.Length == 0)
            {
                stats.Warnings.Add("Skipped a document with no document_id or collection_id.");
                return;
            }

            if (AsBool(doc["is_tombstone"]))
            {
                db.Execute(
                    "DELETE FROM documents WHERE document_id = ? AND collection_id = ? AND layer_id = ?",
                    documentId, collectionId, layerId);
                db.Execute("DELETE FROM collections WHERE collection_id = ?", documentId);
                stats.DocumentsDeleted++;
                return;
            }

            var isArchived = AsBool(doc["is_archived"]);
            var isMoved = AsBool(doc["is_moved"]);
            var payload = doc["payload"];
            var payloadJson = payload == null || payload.Type == JTokenType.Null
                ? "{}"
                : payload.ToString(Formatting.None);

            db.Execute(
                @"INSERT OR REPLACE INTO documents
                  (document_id, collection_id, document_type, layer_id, namespace, identifier,
                   update_key, is_tombstone, is_archived, is_moved, payload)
                  VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?)",
                documentId,
                collectionId,
                (string)doc["document_type"] ?? "",
                layerId,
                (string)doc["namespace"] ?? "document",
                (string)doc["identifier"],
                (long?)doc["update_key"] ?? 0L,
                isArchived ? 1 : 0,
                isMoved ? 1 : 0,
                payloadJson);

            if (isArchived || isMoved)
            {
                stats.DocumentsDeleted++;
            }
            else
            {
                stats.DocumentsUpserted++;
            }
        }

        /// <summary>
        /// Rebuilds the collections table from every active collection manifest.
        /// </summary>
        /// <remarks>
        /// Run after each sync rather than maintained incrementally, so manifests stored
        /// by an earlier run are represented too. Codegen reads this table.
        /// </remarks>
        internal static void RebuildCollections(TarinoiDb db, SyncStats stats)
        {
            var rows = db.Query<DocumentRow>(
                @"SELECT document_id, payload FROM documents
                  WHERE document_type = 'collection-manifest'
                    AND is_tombstone = 0 AND is_archived = 0 AND is_moved = 0");

            var count = 0;
            foreach (var row in rows)
            {
                JObject payload;
                try
                {
                    payload = JObject.Parse(row.Payload);
                }
                catch
                {
                    stats.Warnings.Add($"Collection manifest '{row.DocumentId}' has an unreadable payload.");
                    continue;
                }

                var name = (string)payload["label"] ?? (string)payload["collection_name"] ?? "";
                db.Execute(
                    @"INSERT OR REPLACE INTO collections
                      (collection_id, collection_name, collection_type, payload)
                      VALUES (?, ?, ?, ?)",
                    row.DocumentId, name, (string)payload["collection_type"] ?? "", row.Payload);
                count++;
            }

            stats.CollectionsUpdated = count;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        HttpClient CreateClient(bool skipTlsVerify)
        {
            if (_handler != null)
            {
                // Injected transport: don't dispose something we didn't create.
                return new HttpClient(_handler, disposeHandler: false);
            }

            var handler = new HttpClientHandler();
            if (skipTlsVerify)
            {
                TarinoiLog.Warn("Sync: TLS certificate verification is disabled. "
                                + "Use this only against a local development server.");
                handler.ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true;
            }

            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        }

        internal static Uri AppendCursor(Uri baseUri, string cursor)
        {
            var builder = new UriBuilder(baseUri);
            var escaped = "cursor=" + Uri.EscapeDataString(cursor);
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? escaped
                : builder.Query.TrimStart('?') + "&" + escaped;
            return builder.Uri;
        }

        /// <summary>Turns a failing status into something the user can act on.</summary>
        internal static string HttpErrorHint(HttpStatusCode status)
        {
            switch (status)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return "Sync failed: credentials rejected — check your API token "
                           + "(Tools > Tarinoi > Set API token…)";
                case HttpStatusCode.NotFound:
                    return "Sync failed: project not found — check the API path in "
                           + "Project Settings > Tarinoi";
                default:
                    if ((int)status >= 500)
                    {
                        return "Sync failed: server error — the Tarinoi API may be temporarily "
                               + "unavailable, try again shortly";
                    }

                    return "Sync failed: unexpected response";
            }
        }

        /// <summary>Reads a flag that the feed may send as a bool, a number, or a string.</summary>
        static bool AsBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            switch (token.Type)
            {
                case JTokenType.Boolean:
                    return (bool)token;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return (double)token != 0;
                case JTokenType.String:
                    var text = ((string)token).Trim().ToLowerInvariant();
                    return text == "true" || text == "1";
                default:
                    return false;
            }
        }

        static float Mathf01(float value) => value < 0f ? 0f : value > 0.9f ? 0.9f : value;
    }
}
