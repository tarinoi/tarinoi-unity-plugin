using NUnit.Framework;
using Tarinoi.Sync;

namespace Tarinoi.Tests
{
    public class NdjsonReaderTests
    {
        [Test]
        public void EmptyBodyYieldsNothing()
        {
            var page = NdjsonReader.Parse("");
            Assert.IsEmpty(page.Documents);
            Assert.IsNull(page.Cursor);
        }

        [Test]
        public void NullBodyYieldsNothing()
        {
            var page = NdjsonReader.Parse(null);
            Assert.IsEmpty(page.Documents);
            Assert.IsNull(page.Cursor);
        }

        [Test]
        public void DocumentsOnly()
        {
            var page = NdjsonReader.Parse(
                "{\"document_id\":\"d1\"}\n{\"document_id\":\"d2\"}");

            Assert.AreEqual(2, page.Documents.Count);
            Assert.AreEqual("d1", (string)page.Documents[0]["document_id"]);
            Assert.IsNull(page.Cursor, "no cursor means this was the last page");
        }

        [Test]
        public void DocumentsFollowedByACursor()
        {
            var page = NdjsonReader.Parse(
                "{\"document_id\":\"d1\"}\n{\"cursor\":\"42\"}");

            Assert.AreEqual(1, page.Documents.Count);
            Assert.AreEqual("42", page.Cursor);
        }

        [Test]
        public void CursorOnly()
        {
            var page = NdjsonReader.Parse("{\"cursor\":\"99\"}");
            Assert.IsEmpty(page.Documents);
            Assert.AreEqual("99", page.Cursor);
        }

        [Test]
        public void NumericCursorIsReadAsText()
        {
            Assert.AreEqual("1234", NdjsonReader.Parse("{\"cursor\":1234}").Cursor);
        }

        [Test]
        public void NullCursorEndsPagination()
        {
            Assert.IsNull(NdjsonReader.Parse("{\"cursor\":null}").Cursor);
        }

        [Test]
        public void BlankAndWhitespaceLinesAreIgnored()
        {
            var page = NdjsonReader.Parse(
                "\n  \n{\"document_id\":\"d1\"}\n\n   \n{\"cursor\":\"7\"}\n\n");

            Assert.AreEqual(1, page.Documents.Count);
            Assert.AreEqual("7", page.Cursor);
        }

        [Test]
        public void CarriageReturnsAreTolerated()
        {
            var page = NdjsonReader.Parse("{\"document_id\":\"d1\"}\r\n{\"cursor\":\"7\"}\r\n");
            Assert.AreEqual(1, page.Documents.Count);
            Assert.AreEqual("7", page.Cursor);
        }

        [Test]
        public void MalformedLinesAreSkippedWithoutLosingTheRest()
        {
            // One bad record must not cost the user a whole sync.
            var page = NdjsonReader.Parse(
                "{\"document_id\":\"d1\"}\nnot json at all\n{\"document_id\":\"d2\"}");

            Assert.AreEqual(2, page.Documents.Count);
        }

        [Test]
        public void ADocumentCarryingACursorFieldIsStillADocument()
        {
            // The sentinel is recognised by being an object with *only* a cursor key.
            var page = NdjsonReader.Parse("{\"document_id\":\"d1\",\"cursor\":\"nope\"}");

            Assert.AreEqual(1, page.Documents.Count, "a two-key object is a document");
            Assert.IsNull(page.Cursor);
        }

        [Test]
        public void TheLastCursorLineWins()
        {
            Assert.AreEqual("second",
                NdjsonReader.Parse("{\"cursor\":\"first\"}\n{\"cursor\":\"second\"}").Cursor);
        }

        [Test]
        public void PayloadsSurviveIntact()
        {
            var page = NdjsonReader.Parse(
                "{\"document_id\":\"d1\",\"payload\":{\"nested\":{\"deep\":[1,2,3]}}}");

            var deep = (Newtonsoft.Json.Linq.JArray)page.Documents[0]["payload"]["nested"]["deep"];
            Assert.AreEqual(3, deep.Count);
        }
    }
}
