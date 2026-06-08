using Xunit;
using System.Collections.Generic;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>CREATE CONNECTION discovery in the TUI metadata scanner (multi-line / nested parens).</summary>
    public class MetadataConnectionParseTests
    {
        private static MetadataManager NewManager(out Dictionary<string, IDataSource> conns)
        {
            conns = new Dictionary<string, IDataSource>();
            return new MetadataManager(SystemExecutionContext.Instance, conns);
        }

        [Fact]
        public void RefreshConnections_ParsesMultiLineBlock()
        {
            var mgr = NewManager(out var conns);
            mgr.RefreshConnections(@"
CREATE CONNECTION m AS MSSQL(
  SERVER             = 'srv',
  DATABASE           = 'db',
  TRUSTED_CONNECTION = ON
);");

            Assert.True(conns.ContainsKey("m"));
            Assert.Equal("MSSQL", mgr.GetConnectionType("m"));
        }

        [Fact]
        public void RefreshConnections_HandlesNestedParensInValue()
        {
            var mgr = NewManager(out var conns);
            // A value containing ')' would truncate a naive [^)]* capture.
            mgr.RefreshConnections(@"CREATE CONNECTION m AS MSSQL(
  SERVER   = '(local)',
  DATABASE = 'db'
);");

            Assert.True(conns.ContainsKey("m"));
            Assert.Equal("MSSQL", mgr.GetConnectionType("m"));
        }

        [Fact]
        public void RefreshConnections_ParsesTwoBlocksIndependently()
        {
            var mgr = NewManager(out var conns);
            mgr.RefreshConnections(@"
CREATE CONNECTION a AS MSSQL( SERVER = 's1' );
CREATE CONNECTION b AS POSTGRES( SERVER = 's2' );");

            Assert.Equal("MSSQL", mgr.GetConnectionType("a"));
            Assert.Equal("POSTGRES", mgr.GetConnectionType("b"));
        }
    }
}
