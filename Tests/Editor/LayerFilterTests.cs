using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tarinoi.Data;

namespace Tarinoi.Tests
{
    /// <summary>
    /// The two-layer merge, exercised through both of its forms.
    /// </summary>
    /// <remarks>
    /// The Godot plugin has no tests for this at all, and its two implementations
    /// drifted apart as a result. <see cref="BothFormsAgree"/> is the test that would
    /// have caught that.
    /// </remarks>
    public class LayerFilterTests
    {
        static DocumentRow Row(string layerId, string documentId = "d1", string collectionId = "c1",
            bool tombstone = false, bool archived = false, bool moved = false, string payload = null)
        {
            return new DocumentRow
            {
                DocumentId = documentId,
                CollectionId = collectionId,
                LayerId = layerId,
                DocumentType = "card",
                UpdateKey = 1,
                IsTombstone = tombstone ? 1 : 0,
                IsArchived = archived ? 1 : 0,
                IsMoved = moved ? 1 : 0,
                Payload = payload ?? layerId,
            };
        }

        // ---------------------------------------------------------------------
        // In-memory merge
        // ---------------------------------------------------------------------

        [Test]
        public void MainOnlyAndActiveIsVisible()
        {
            var merged = LayerFilter.Merge(new[] { Row(LayerFilter.MainLayer) }, false);
            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(LayerFilter.MainLayer, merged[0].LayerId);
        }

        [Test]
        public void MainOnlyAndInactiveIsHidden()
        {
            Assert.IsEmpty(LayerFilter.Merge(new[] { Row(LayerFilter.MainLayer, archived: true) }, false));
            Assert.IsEmpty(LayerFilter.Merge(new[] { Row(LayerFilter.MainLayer, tombstone: true) }, false));
            Assert.IsEmpty(LayerFilter.Merge(new[] { Row(LayerFilter.MainLayer, moved: true) }, false));
        }

        [Test]
        public void ActiveBufferOverridesMain()
        {
            var merged = LayerFilter.Merge(
                new[] { Row(LayerFilter.MainLayer), Row(LayerFilter.BufferLayer) }, false);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(LayerFilter.BufferLayer, merged[0].LayerId,
                "an uncommitted edit must win over the committed version");
        }

        [Test]
        public void InactiveBufferSuppressesMainEntirely()
        {
            // This is the subtle one: the buffer row is a deletion marker, so the
            // main row must NOT reappear as a fallback.
            var merged = LayerFilter.Merge(
                new[] { Row(LayerFilter.MainLayer), Row(LayerFilter.BufferLayer, archived: true) }, false);

            Assert.IsEmpty(merged, "an archived buffer row deletes the document, it doesn't revert it");
        }

        [Test]
        public void TombstonedBufferSuppressesMainEntirely()
        {
            var merged = LayerFilter.Merge(
                new[] { Row(LayerFilter.MainLayer), Row(LayerFilter.BufferLayer, tombstone: true) }, false);

            Assert.IsEmpty(merged, "the GDScript merge misses this case — it only checks archived/moved");
        }

        [Test]
        public void BufferOnlyAndActiveIsVisible()
        {
            var merged = LayerFilter.Merge(new[] { Row(LayerFilter.BufferLayer) }, false);
            Assert.AreEqual(1, merged.Count);
        }

        [Test]
        public void BufferOnlyAndInactiveIsHidden()
        {
            Assert.IsEmpty(LayerFilter.Merge(new[] { Row(LayerFilter.BufferLayer, moved: true) }, false));
        }

        [Test]
        public void CommittedOnlyIgnoresTheBufferLayerCompletely()
        {
            var rows = new[] { Row(LayerFilter.MainLayer), Row(LayerFilter.BufferLayer) };
            var merged = LayerFilter.Merge(rows, true);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(LayerFilter.MainLayer, merged[0].LayerId);
        }

        [Test]
        public void CommittedOnlyShowsMainEvenWhenBufferWouldSuppressIt()
        {
            var rows = new[] { Row(LayerFilter.MainLayer), Row(LayerFilter.BufferLayer, archived: true) };
            var merged = LayerFilter.Merge(rows, true);

            Assert.AreEqual(1, merged.Count,
                "an uncommitted deletion must not affect the committed view");
        }

        [Test]
        public void DocumentsAreMergedIndependently()
        {
            var rows = new[]
            {
                Row(LayerFilter.MainLayer, "d1"),
                Row(LayerFilter.BufferLayer, "d1", archived: true),
                Row(LayerFilter.MainLayer, "d2"),
                Row(LayerFilter.BufferLayer, "d3"),
            };

            var ids = LayerFilter.Merge(rows, false).Select(r => r.DocumentId).ToList();
            CollectionAssert.AreEquivalent(new[] { "d2", "d3" }, ids);
        }

        [Test]
        public void SameDocumentIdInDifferentCollectionsDoesNotCollide()
        {
            var rows = new[]
            {
                Row(LayerFilter.MainLayer, "shared", "c1"),
                Row(LayerFilter.BufferLayer, "shared", "c2", archived: true),
            };

            var merged = LayerFilter.Merge(rows, false);
            Assert.AreEqual(1, merged.Count, "c2's deletion must not remove c1's document");
            Assert.AreEqual("c1", merged[0].CollectionId);
        }

        [Test]
        public void InputOrderIsPreserved()
        {
            var rows = new[]
            {
                Row(LayerFilter.MainLayer, "b"),
                Row(LayerFilter.MainLayer, "a"),
                Row(LayerFilter.MainLayer, "c"),
            };

            var ids = LayerFilter.Merge(rows, false).Select(r => r.DocumentId).ToList();
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, ids);
        }

        [Test]
        public void UnknownLayersPassThroughWhenActive()
        {
            // A future layer should degrade to visible rather than silently vanishing.
            var merged = LayerFilter.Merge(new[] { Row("tarinoi:some-future-layer") }, false);
            Assert.AreEqual(1, merged.Count);
        }

        [Test]
        public void EmptyInputProducesEmptyOutput()
        {
            Assert.IsEmpty(LayerFilter.Merge(new DocumentRow[0], false));
        }

        // ---------------------------------------------------------------------
        // SQL form, and agreement between the two
        // ---------------------------------------------------------------------

        /// <summary>Every combination of layer presence and activity flags.</summary>
        static IEnumerable<TestCaseData> LayerCases()
        {
            yield return new TestCaseData(true, false, false, false, false).SetName("main_active_only");
            yield return new TestCaseData(true, true, false, false, false).SetName("main_inactive_only");
            yield return new TestCaseData(false, false, true, false, false).SetName("buffer_active_only");
            yield return new TestCaseData(false, false, true, true, false).SetName("buffer_inactive_only");
            yield return new TestCaseData(true, false, true, false, false).SetName("both_active");
            yield return new TestCaseData(true, false, true, true, false).SetName("active_main_inactive_buffer");
            yield return new TestCaseData(true, true, true, false, false).SetName("inactive_main_active_buffer");
            yield return new TestCaseData(true, true, true, true, false).SetName("both_inactive");
            yield return new TestCaseData(true, false, true, true, true).SetName("committed_only_ignores_buffer");
            yield return new TestCaseData(true, true, true, false, true).SetName("committed_only_inactive_main");
        }

        [TestCaseSource(nameof(LayerCases))]
        public void BothFormsAgree(bool hasMain, bool mainInactive, bool hasBuffer, bool bufferInactive,
            bool committedOnly)
        {
            var rows = new List<DocumentRow>();
            using (var fixture = new TestDb(committedOnly))
            {
                if (hasMain)
                {
                    rows.Add(Row(LayerFilter.MainLayer, archived: mainInactive));
                    fixture.InsertDocument("d1", "c1", LayerFilter.MainLayer,
                        archived: mainInactive, payload: LayerFilter.MainLayer);
                }

                if (hasBuffer)
                {
                    rows.Add(Row(LayerFilter.BufferLayer, archived: bufferInactive));
                    fixture.InsertDocument("d1", "c1", LayerFilter.BufferLayer,
                        archived: bufferInactive, payload: LayerFilter.BufferLayer);
                }

                var fromSql = fixture.VisiblePayloads();
                var fromMemory = LayerFilter.Merge(rows, committedOnly).Select(r => r.Payload).ToList();

                CollectionAssert.AreEqual(fromSql, fromMemory,
                    "the SQL filter and the in-memory merge must select the same documents");
            }
        }

        [Test]
        public void SqlFilterHandlesEachInactivityFlag()
        {
            using (var fixture = new TestDb())
            {
                fixture.InsertDocument("active");
                fixture.InsertDocument("tombstoned", tombstone: true);
                fixture.InsertDocument("archived", archived: true);
                fixture.InsertDocument("moved", moved: true);

                CollectionAssert.AreEqual(new[] { "active" }, fixture.VisibleDocumentIds());
            }
        }
    }
}
