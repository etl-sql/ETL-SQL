using Xunit;
using ETL_SQL.Services;
using ETL_SQL.Common;
using System;
using System.Threading.Tasks;

namespace ETL_SQL.Tests
{
    public class EncryptionTests
    {
        [Fact]
        public void TestEncryption_Basic()
        {
            var security = new SecurityService(NullLogger.Instance) { MasterPassword = "StrongPassword" };
            
            var original = "CREATE CONNECTION my_mock ON MOCKDB('dummy_conn_str');";
            var encrypted = security.EncryptScript(original, "StrongPassword");
            
            // Should contain ENC: prefix
            Assert.Contains("ENC:", encrypted);
            Assert.DoesNotContain("dummy_conn_str", encrypted);
            
            var decrypted = security.DecryptScript(encrypted, "StrongPassword");
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void TestEncryption_WithOverride()
        {
            var security = new SecurityService(NullLogger.Instance) { MasterPassword = "StrongPassword" };
            
            // Explicitly disable encryption
            var original = "CREATE CONNECTION my_mock ON MOCKDB('dummy_conn_str') WITH (ENCRYPT=OFF);";
            var encrypted = security.EncryptScript(original, "StrongPassword");
            
            // Should be unchanged
            Assert.Equal(original, encrypted);
            Assert.DoesNotContain("ENC:", encrypted);
        }

        [Fact]
        public void TestEncryption_PartialScript()
        {
            var security = new SecurityService(NullLogger.Instance);
            
            var original = @"
                -- This should be encrypted
                CREATE CONNECTION c1 ON MOCKDB('secret1');
                
                -- This should NOT be encrypted
                CREATE CONNECTION c2 ON MOCKDB('plain2') WITH (ENCRYPT=OFF);
            ";
            
            var encrypted = security.EncryptScript(original, "pwd");
            
            Assert.Contains("ENC:", encrypted);
            Assert.Contains("plain2", encrypted);
            Assert.Contains("ENCRYPT=OFF", encrypted);
            
            var decrypted = security.DecryptScript(encrypted, "pwd");
            // Normalize whitespace for comparison if needed, but here simple contains check is safer
            Assert.Contains("secret1", decrypted);
        }
    }
}
