using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    public class ETLSuggestEngineTests
    {
        [Fact]
        public void HighlightLine_ApiKey_ShouldBeMasked()
        {
            string line = "CREATE CONNECTION orch AS ORCHESTRATOR(API_KEY='MyAdminpass1!');";
            bool endsInMultiline;
            string highlighted = ETLSuggestEngine.HighlightLine(line, 0, 100, false, out endsInMultiline);

            // It should NOT contain the plain text password
            Assert.DoesNotContain("MyAdminpass1!", highlighted);
            // It SHOULD contain some asterisks
            Assert.Contains("********", highlighted);
        }

        [Fact]
        public void HighlightLine_EncryptedPassword_ShouldBeMasked()
        {
            string line = "CREATE CONNECTION portal AS REPORTPORTAL(PASSWORD='ENC:AcXpkzRv...');";
            bool endsInMultiline;
            string highlighted = ETLSuggestEngine.HighlightLine(line, 0, 100, false, out endsInMultiline);

            Assert.DoesNotContain("ENC:AcXpkzRv", highlighted);
            Assert.Contains("********", highlighted);
        }

        [Fact]
        public void HighlightLine_NormalString_ShouldNotBeMasked()
        {
            string line = "SELECT 'Hello World' FROM MyTable;";
            bool endsInMultiline;
            string highlighted = ETLSuggestEngine.HighlightLine(line, 0, 100, false, out endsInMultiline);

            Assert.Contains("Hello World", highlighted);
        }

        [Fact]
        public void HighlightLine_MaskingPreservesLengthForCursorStability()
        {
            string line = "SET @pass = 'secret';";
            // In this case 'secret' follows @pass which is NOT in our sensitive list
            // Let's test one that IS.
            line = "CREATE CONNECTION c AS MSSQL(PASSWORD='1234567890');";
            bool endsInMultiline;
            string highlighted = ETLSuggestEngine.HighlightLine(line, 0, 100, false, out endsInMultiline);

            // '1234567890' is 12 chars including quotes.
            // Masked should also be 12 chars.
            // Expected: '**********'

            // We need to strip Spectre markup to check length
            string plain = highlighted.Replace("[darkorange3]", "").Replace("[/]", "");
            Assert.Contains("'**********'", plain);
        }
    }
}
