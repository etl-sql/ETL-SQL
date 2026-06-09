using Xunit;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Unit tests for <see cref="MemoryGrantArbiter"/> — the process-wide grant pool that bounds
    /// total committed buffer memory across concurrent queries (Gap F).
    /// </summary>
    public class MemoryGrantArbiterTests
    {
        [Fact]
        public void UnboundedBudget_NeverForcesSpill()
        {
            var arbiter = new MemoryGrantArbiter(0); // 0 = unbounded
            using var lease = arbiter.AcquireLease();
            Assert.False(lease.RegisterAndCheckSpill(long.MaxValue / 2));
            Assert.Equal(0, arbiter.ReservedBytes);
        }

        [Fact]
        public void WithinBudget_Reserves_AndReportsUsage()
        {
            var arbiter = new MemoryGrantArbiter(1000);
            using var lease = arbiter.AcquireLease();

            Assert.False(lease.RegisterAndCheckSpill(400));
            Assert.Equal(400, arbiter.ReservedBytes);
        }

        [Fact]
        public void SecondConcurrentQuery_OverBudget_IsToldToSpill_AndDoesNotReserve()
        {
            var arbiter = new MemoryGrantArbiter(1000);
            using var q1 = arbiter.AcquireLease();
            using var q2 = arbiter.AcquireLease();

            Assert.False(q1.RegisterAndCheckSpill(700));   // fits: 0 + 700 <= 1000
            Assert.True(q2.RegisterAndCheckSpill(700));     // 700 + 700 > 1000 → spill
            // The spilling query must not have grown the reserved total.
            Assert.Equal(700, arbiter.ReservedBytes);
        }

        [Fact]
        public void SmallQuery_StillFits_WhenLargeOneHasReserved()
        {
            var arbiter = new MemoryGrantArbiter(1000);
            using var big = arbiter.AcquireLease();
            using var small = arbiter.AcquireLease();

            Assert.False(big.RegisterAndCheckSpill(700));
            Assert.False(small.RegisterAndCheckSpill(200));  // 700 + 200 <= 1000
            Assert.Equal(900, arbiter.ReservedBytes);
        }

        [Fact]
        public void DisposingLease_ReleasesFootprint()
        {
            var arbiter = new MemoryGrantArbiter(1000);
            var q1 = arbiter.AcquireLease();
            Assert.False(q1.RegisterAndCheckSpill(800));
            Assert.Equal(800, arbiter.ReservedBytes);

            q1.Dispose();
            Assert.Equal(0, arbiter.ReservedBytes);

            // A new query can now use the full budget.
            using var q2 = arbiter.AcquireLease();
            Assert.False(q2.RegisterAndCheckSpill(900));
        }

        [Fact]
        public void Footprint_GrowsButDoesNotShrink_WithinASingleLease()
        {
            var arbiter = new MemoryGrantArbiter(1000);
            using var lease = arbiter.AcquireLease();

            Assert.False(lease.RegisterAndCheckSpill(300));
            Assert.Equal(300, arbiter.ReservedBytes);

            Assert.False(lease.RegisterAndCheckSpill(100)); // smaller — no change
            Assert.Equal(300, arbiter.ReservedBytes);

            Assert.False(lease.RegisterAndCheckSpill(500)); // larger — grows
            Assert.Equal(500, arbiter.ReservedBytes);
        }
    }
}
