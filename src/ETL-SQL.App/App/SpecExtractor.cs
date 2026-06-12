using System;
using System.IO;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace ETL_SQL.App
{
    public class SpecExtractor
    {
        public static int Extract(string inputPath, string outputPath, ILogger logger)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    logger.WriteLine($"Input file not found: {inputPath}", ConsoleColor.Red);
                    return 1;
                }

                logger.WriteLine($"Scanning specification: {inputPath}");

                // Open the source PDF document in import mode
                using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                using var outputDocument = new PdfDocument();

                int pagesKept = 0;
                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    var page = inputDocument.Pages[i];
                    string pageText = ExtractTextFromPage(page);

                    int score = CalculateSchemaScore(pageText);

                    // If the page contains a high density of schema/dictionary content, keep it
                    if (score >= 15)
                    {
                        logger.WriteLine($"  -> Page {i + 1}: Flagged as Schema (Score: {score})");
                        outputDocument.AddPage(page);
                        pagesKept++;
                    }
                }

                if (pagesKept == 0)
                {
                    logger.WriteLine("No schema or data dictionary tables were identified in the PDF.", ConsoleColor.Yellow);
                    return 1;
                }

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                outputDocument.Save(outputPath);
                logger.WriteLine($"Specification trimmed: {pagesKept} of {inputDocument.PageCount} pages written to {outputPath}", ConsoleColor.Green);
                return 0;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Error scanning PDF: {ex.Message}", ConsoleColor.Red);
                logger.WriteLine(ex.ToString(), ConsoleColor.DarkGray);
                return 1;
            }
        }

        private static int CalculateSchemaScore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int score = 0;

            // 1. High-value database types (+5 each)
            string[] dbTypes = { "VARCHAR", "INT", "INTEGER", "DECIMAL", "DATETIME", "BIT", "FLOAT", "NUMERIC", "TIMESTAMP", "SMALLINT", "BIGINT" };
            foreach (var type in dbTypes)
            {
                score += Regex.Matches(text, "\\b" + type + "\\b", RegexOptions.IgnoreCase).Count * 5;
            }

            // 2. High-value layout columns (+3 each)
            string[] schemaHeaders = { "FIELD NAME", "COLUMN NAME", "DATA TYPE", "NULLABLE", "MANDATORY", "REQUIRED", "DESCRIPTION" };
            foreach (var header in schemaHeaders)
            {
                score += Regex.Matches(text, header, RegexOptions.IgnoreCase).Count * 3;
            }

            // 3. Administrative noise penalty (-5 each)
            string[] noiseWords = { "TABLE OF CONTENTS", "REVISION HISTORY", "OAUTH", "AUTHENTICATION", "WHITELIST", "CHANGELOG", "SUPPORT CONTACT" };
            foreach (var noise in noiseWords)
            {
                score -= Regex.Matches(text, noise, RegexOptions.IgnoreCase).Count * 5;
            }

            return score;
        }

        private static string ExtractTextFromPage(PdfPage page)
        {
            try
            {
                var sequence = ContentReader.ReadContent(page);
                return GetTextFromObject(sequence);
            }
            catch
            {
                // Fallback if content reader fails
                return "";
            }
        }

        private static string GetTextFromObject(CObject obj)
        {
            if (obj is CSequence seq)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var child in seq)
                {
                    sb.Append(GetTextFromObject(child));
                }
                return sb.ToString();
            }
            if (obj is CArray arr)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var child in arr)
                {
                    sb.Append(GetTextFromObject(child));
                }
                return sb.ToString();
            }
            if (obj is COperator op)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var operand in op.Operands)
                {
                    sb.Append(GetTextFromObject(operand));
                }
                return sb.ToString();
            }
            if (obj is CString str)
            {
                return str.Value + " ";
            }
            return "";
        }
    }
}
