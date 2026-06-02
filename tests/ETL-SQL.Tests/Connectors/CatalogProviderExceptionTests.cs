using System.Threading.Tasks;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    public class CatalogProviderExceptionTests
    {
        [Fact]
        public async Task SqlServerCatalogProvider_WrapsProviderExceptions()
        {
            var provider = new SqlServerCatalogProvider("");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                provider.GetColumnMetadataAsync("dbo", "Customers"));

            Assert.Contains("SQL Server catalog connector error", ex.Message);
        }

        [Fact]
        public async Task PostgresCatalogProvider_WrapsProviderExceptions()
        {
            var provider = new PostgresCatalogProvider("");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                provider.GetColumnMetadataAsync("public", "customers"));

            Assert.Contains("PostgreSQL catalog connector error", ex.Message);
        }

        [Fact]
        public async Task MySqlCatalogProvider_WrapsProviderExceptions()
        {
            var provider = new MySqlCatalogProvider("");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                provider.GetColumnMetadataAsync("app", "customers"));

            Assert.Contains("MySql catalog connector error", ex.Message);
        }
    }
}
