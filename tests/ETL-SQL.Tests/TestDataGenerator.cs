using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Generates binary test data files (xlsx, etc.) that cannot be committed as text.
    /// Call GenerateAll() once during test setup if the files are missing.
    /// </summary>
    public static class TestDataGenerator
    {
        private static readonly string TestDataDir =
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestData");

        public static void EnsureTestDataFiles()
        {
            var dir = Path.GetFullPath(TestDataDir);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var xlsx = Path.Combine(dir, "test_employees.xlsx");
            if (!File.Exists(xlsx)) GenerateEmployeesXlsx(xlsx);
        }

        /// <summary>
        /// Creates a minimal valid .xlsx (Office Open XML) with an Employees sheet.
        /// Uses only System.IO.Compression — no external dependencies.
        /// </summary>
        private static void GenerateEmployeesXlsx(string path)
        {
            // Shared strings (column headers + string values)
            var sharedStrings = new[]
            {
                "ID", "Name", "Department", "Salary", "HireDate",
                "Alice", "Bob", "Charlie", "Diana", "Eve",
                "Engineering", "Marketing", "Finance"
            };

            // Build sheet1.xml rows
            var rows = new (string id, string name, string dept, decimal salary, string hire)[]
            {
                ("1", "Alice",   "Engineering", 95000m, "2020-01-15"),
                ("2", "Bob",     "Marketing",   72000m, "2019-06-01"),
                ("3", "Charlie", "Engineering", 88000m, "2021-03-22"),
                ("4", "Diana",   "Finance",     81000m, "2018-11-30"),
                ("5", "Eve",     "Marketing",   67000m, "2022-07-08"),
            };

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");

            // Header row
            sb.Append("<row r=\"1\">");
            string[] cols = { "A", "B", "C", "D", "E" };
            string[] headers = { "ID", "Name", "Department", "Salary", "HireDate" };
            for (int c = 0; c < headers.Length; c++)
            {
                int si = Array.IndexOf(sharedStrings, headers[c]);
                sb.Append($"<c r=\"{cols[c]}1\" t=\"s\"><v>{si}</v></c>");
            }
            sb.Append("</row>");

            // Data rows
            for (int r = 0; r < rows.Length; r++)
            {
                int rowNum = r + 2;
                var (id, name, dept, salary, hire) = rows[r];
                sb.Append($"<row r=\"{rowNum}\">");
                sb.Append($"<c r=\"A{rowNum}\"><v>{id}</v></c>");
                sb.Append($"<c r=\"B{rowNum}\" t=\"s\"><v>{Array.IndexOf(sharedStrings, name)}</v></c>");
                sb.Append($"<c r=\"C{rowNum}\" t=\"s\"><v>{Array.IndexOf(sharedStrings, dept)}</v></c>");
                sb.Append($"<c r=\"D{rowNum}\"><v>{salary}</v></c>");
                sb.Append($"<c r=\"E{rowNum}\" t=\"s\"><v>{Array.IndexOf(sharedStrings, hire)}</v></c>");
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            string sheet1 = sb.ToString();

            // Shared strings XML
            var ssSb = new StringBuilder();
            ssSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            ssSb.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{sharedStrings.Length}\" uniqueCount=\"{sharedStrings.Length}\">");
            foreach (var s in sharedStrings)
                ssSb.Append($"<si><t>{s}</t></si>");
            ssSb.Append("</sst>");

            using var fs = new FileStream(path, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            void AddEntry(string name, string content)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                w.Write(content);
            }

            AddEntry("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            AddEntry("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            AddEntry("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");

            AddEntry("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Employees\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>");

            AddEntry("xl/worksheets/sheet1.xml", sheet1);
            AddEntry("xl/sharedStrings.xml", ssSb.ToString());
            AddEntry("xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills><fill><patternFill patternType=\"none\"/></fill></fills>" +
                "<borders><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                "</styleSheet>");
        }
    }
}
