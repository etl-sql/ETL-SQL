using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class TransactionHardeningTests
    {
        [Fact]
        public async Task TransactionManager_ShouldHandleNestedTransactions()
        {
            // Arrange
            var mgr = new TransactionManager();
            var variables = new Dictionary<string, object?> { ["@val"] = 1 };
            var connections = new Dictionary<string, IDataSource>();

            // Act
            await mgr.BeginTransaction(variables, connections); // Level 1
            variables["@val"] = 2;
            await mgr.BeginTransaction(variables, connections); // Level 2
            variables["@val"] = 3;

            // Assert nested state
            Assert.Equal(2, mgr.TranCount);

            // Act - Commit Level 2
            await mgr.CommitTransaction();
            Assert.Equal(1, mgr.TranCount);
            Assert.Equal(3, variables["@val"]); // Commits don't revert variables

            // Act - Commit Level 1
            await mgr.CommitTransaction();
            Assert.Equal(0, mgr.TranCount);
        }

        [Fact]
        public async Task TransactionManager_RollbackAll_ShouldRevertToRoot()
        {
            // Arrange
            var mgr = new TransactionManager();
            var variables = new Dictionary<string, object?> { ["@val"] = 100 };
            var connections = new Dictionary<string, IDataSource>();

            // Level 1
            await mgr.BeginTransaction(variables, connections);
            variables["@val"] = 200;

            // Level 2
            await mgr.BeginTransaction(variables, connections);
            variables["@val"] = 300;

            // Act - Rollback All (triggered by any rollback in this impl)
            await mgr.RollbackAll(variables, connections);

            // Assert
            Assert.Equal(0, mgr.TranCount);
            Assert.Equal(100, variables["@val"]); // Reverted to pre-transaction root
        }

        [Fact]
        public async Task TransactionManager_ShouldEnlistExternalDataSource()
        {
            // Arrange
            var mgr = new TransactionManager();
            var variables = new Dictionary<string, object?>();
            var connections = new Dictionary<string, IDataSource>();

            var mockDs = new Mock<ITransactionalDataSource>();

            // Act
            await mgr.BeginTransaction(variables, connections);
            await mgr.EnlistDataSource(mockDs.Object);
            await mgr.CommitTransaction();

            // Assert
            mockDs.Verify(m => m.BeginTransactionAsync(), Times.Once);
            mockDs.Verify(m => m.CommitAsync(), Times.Once);
        }
    }
}
