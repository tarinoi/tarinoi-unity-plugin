using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tarinoi.Sync
{
    /// <summary>One page of the documents feed: the documents, plus the next cursor.</summary>
    public sealed class NdjsonPage
    {
        public readonly List<JObject> Documents = new List<JObject>();

        /// <summary>
        /// The pagination cursor, or null when the server signalled the last page.
        /// A null cursor is what ends the sync loop.
        /// </summary>
        public string Cursor;
    }

    /// <summary>
    /// Parses the newline-delimited JSON the documents endpoint returns.
    /// </summary>
    /// <remarks>
    /// Every line is a JSON object. A line that is an object with exactly one key,
    /// <c>cursor</c>, is the pagination sentinel rather than a document — that
    /// "exactly one key" rule is what keeps a document that happens to carry a
    /// <c>cursor</c> field from being swallowed.
    /// <para>
    /// Unparseable lines are skipped rather than failing the page, matching the Godot
    /// importer: one malformed record should not cost the user an entire sync.
    /// </para>
    /// </remarks>
    public static class NdjsonReader
    {
        /// <summary>Parses a complete NDJSON body held in memory.</summary>
        public static NdjsonPage Parse(string body)
        {
            var page = new NdjsonPage();
            if (string.IsNullOrEmpty(body))
            {
                return page;
            }

            using (var reader = new StringReader(body))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    AddLine(page, line);
                }
            }

            return page;
        }

        /// <summary>
        /// Parses NDJSON directly from a response stream, so a large page never exists
        /// as one giant string.
        /// </summary>
        public static async Task<NdjsonPage> ParseAsync(Stream stream, CancellationToken ct = default)
        {
            var page = new NdjsonPage();
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    AddLine(page, line);
                }
            }

            return page;
        }

        static void AddLine(NdjsonPage page, string rawLine)
        {
            var line = rawLine?.Trim();
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            JObject obj;
            try
            {
                obj = JObject.Parse(line);
            }
            catch
            {
                // Skipped deliberately: see the class remarks.
                return;
            }

            if (obj.Count == 1 && obj["cursor"] != null)
            {
                page.Cursor = obj["cursor"].Type == JTokenType.Null ? null : obj["cursor"].ToString();
                return;
            }

            page.Documents.Add(obj);
        }
    }
}
