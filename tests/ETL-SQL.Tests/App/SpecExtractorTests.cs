using System;
using System.IO;
using Xunit;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.App
{
    public class SpecExtractorTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _inputPdf;
        private readonly string _outputPdf;

        public SpecExtractorTests()
        {
            // Initialize PDFsharp font resolver
            try
            {
                new PdfExporter().Export(new ReportManifest { Title = "Font Init" });
            }
            catch
            {
                // Ignore any rendering errors; we only need to trigger font initialization
            }

            _tempDir = Path.Combine(Path.GetTempPath(), "etlsql_spec_extractor_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _inputPdf = Path.Combine(_tempDir, "input.pdf");
            _outputPdf = Path.Combine(_tempDir, "output.pdf");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public void Extract_MissingInputFile_ReturnsError()
        {
            var nonExistent = Path.Combine(_tempDir, "missing.pdf");
            var result = SpecExtractor.Extract(nonExistent, _outputPdf, NullLogger.Instance);
            Assert.Equal(1, result);
        }

        [Fact]
        public void Extract_FiltersAdministrativeFluffAndKeepsSchema()
        {
            // 1. Create a PDF document with three pages
            using (var document = new PdfDocument())
            {
                // Page 1: Administrative noise (Table of contents, Changelog, Revision History)
                var page1 = document.AddPage();
                using (var gfx = XGraphics.FromPdfPage(page1))
                {
                    var font = new XFont("Arial", 12);
                    gfx.DrawString("TABLE OF CONTENTS. REVISION HISTORY. CHANGELOG. SUPPORT CONTACT.", font, XBrushes.Black, new XRect(10, 10, page1.Width.Point, page1.Height.Point), XStringFormats.TopLeft);
                }

                // Page 2: Schema Page (High density of VARCHAR, INT, COLUMN NAME)
                var page2 = document.AddPage();
                using (var gfx = XGraphics.FromPdfPage(page2))
                {
                    var font = new XFont("Arial", 12);
                    gfx.DrawString("COLUMN NAME, FIELD NAME, DATA TYPE, NULLABLE. CustomerId INT. Email VARCHAR. Address VARCHAR.", font, XBrushes.Black, new XRect(10, 10, page2.Width.Point, page2.Height.Point), XStringFormats.TopLeft);
                }

                // Page 3: Connectivity Setup & OAuth (Administrative noise)
                var page3 = document.AddPage();
                using (var gfx = XGraphics.FromPdfPage(page3))
                {
                    var font = new XFont("Arial", 12);
                    gfx.DrawString("OAUTH, AUTHENTICATION, WHITELIST, SECURITY CONFIGURATION.", font, XBrushes.Black, new XRect(10, 10, page3.Width.Point, page3.Height.Point), XStringFormats.TopLeft);
                }

                document.Save(_inputPdf);
            }

            // 2. Run SpecExtractor on the input PDF
            var result = SpecExtractor.Extract(_inputPdf, _outputPdf, NullLogger.Instance);

            // 3. Assert successes and outputs
            Assert.Equal(0, result);
            Assert.True(File.Exists(_outputPdf));

            // 4. Open generated output PDF and assert it has only 1 page (the schema page)
            using (var outputDocument = PdfSharp.Pdf.IO.PdfReader.Open(_outputPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                Assert.Equal(1, outputDocument.PageCount);
            }
        }
    }
}
