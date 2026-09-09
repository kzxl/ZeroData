using System;
using Xunit;
using ZeroData.Core;

namespace ZeroData.Tests
{
    public class DataFrameJoinTests
    {
        [Fact]
        public void TestInnerJoin()
        {
            var left = new DataFrame(
                new DataColumn<int>("Id", new[] { 1, 2, 3 }),
                new DataColumn<string>("Name", new[] { "Alice", "Bob", "Charlie" })
            );

            var right = new DataFrame(
                new DataColumn<int>("Id", new[] { 2, 3, 4 }),
                new DataColumn<double>("Score", new[] { 85.5, 92.0, 78.0 })
            );

            var joined = left.Join(right, "Id", JoinType.Inner);

            Assert.Equal(2, joined.RowCount);
            Assert.Equal(3, joined.ColumnCount); // Id, Name, Score

            var idCol = (DataColumn<int>)joined["Id"];
            var nameCol = (DataColumn<string>)joined["Name"];
            var scoreCol = (DataColumn<double>)joined["Score"];

            Assert.Equal(2, idCol[0]);
            Assert.Equal("Bob", nameCol[0]);
            Assert.Equal(85.5, scoreCol[0]);

            Assert.Equal(3, idCol[1]);
            Assert.Equal("Charlie", nameCol[1]);
            Assert.Equal(92.0, scoreCol[1]);
        }

        [Fact]
        public void TestLeftJoin()
        {
            var left = new DataFrame(
                new DataColumn<int>("Id", new[] { 1, 2, 3 }),
                new DataColumn<string>("Name", new[] { "Alice", "Bob", "Charlie" })
            );

            var right = new DataFrame(
                new DataColumn<int>("Id", new[] { 2, 3, 4 }),
                new DataColumn<double>("Score", new[] { 85.5, 92.0, 78.0 })
            );

            var joined = left.Join(right, "Id", JoinType.Left);

            Assert.Equal(3, joined.RowCount);

            var idCol = (DataColumn<int>)joined["Id"];
            var nameCol = (DataColumn<string>)joined["Name"];
            var scoreCol = (DataColumn<double>)joined["Score"];

            // Row 0: Id 1 (Alice), Score = default(double) = 0.0
            Assert.Equal(1, idCol[0]);
            Assert.Equal("Alice", nameCol[0]);
            Assert.Equal(0.0, scoreCol[0]);

            // Row 1: Id 2 (Bob), Score = 85.5
            Assert.Equal(2, idCol[1]);
            Assert.Equal("Bob", nameCol[1]);
            Assert.Equal(85.5, scoreCol[1]);
        }

        [Fact]
        public void TestRightJoin()
        {
            var left = new DataFrame(
                new DataColumn<int>("Id", new[] { 1, 2 }),
                new DataColumn<string>("Name", new[] { "Alice", "Bob" })
            );

            var right = new DataFrame(
                new DataColumn<int>("Id", new[] { 2, 3 }),
                new DataColumn<double>("Score", new[] { 85.5, 95.0 })
            );

            var joined = left.Join(right, "Id", JoinType.Right);

            Assert.Equal(2, joined.RowCount);

            var nameCol = (DataColumn<string>)joined["Name"];
            var scoreCol = (DataColumn<double>)joined["Score"];

            // Matched row (Id 2)
            Assert.Equal("Bob", nameCol[0]);
            Assert.Equal(85.5, scoreCol[0]);

            // Unmatched right row (Id 3)
            Assert.Null(nameCol[1]);
            Assert.Equal(95.0, scoreCol[1]);
        }

        [Fact]
        public void TestFullOuterJoin()
        {
            var left = new DataFrame(
                new DataColumn<int>("Id", new[] { 1, 2 }),
                new DataColumn<string>("Name", new[] { "Alice", "Bob" })
            );

            var right = new DataFrame(
                new DataColumn<int>("Id", new[] { 2, 3 }),
                new DataColumn<double>("Score", new[] { 85.5, 95.0 })
            );

            var joined = left.Join(right, "Id", JoinType.FullOuter);

            Assert.Equal(3, joined.RowCount); // Id 1, Id 2, Id 3

            var idCol = (DataColumn<int>)joined["Id"];
            var nameCol = (DataColumn<string>)joined["Name"];
            var scoreCol = (DataColumn<double>)joined["Score"];

            // Row 0: Id 1 (Alice, 0.0)
            Assert.Equal(1, idCol[0]);
            Assert.Equal("Alice", nameCol[0]);
            Assert.Equal(0.0, scoreCol[0]);

            // Row 1: Id 2 (Bob, 85.5)
            Assert.Equal(2, idCol[1]);
            Assert.Equal("Bob", nameCol[1]);
            Assert.Equal(85.5, scoreCol[1]);

            // Row 2: Id 3 (null, 95.0)
            Assert.Null(nameCol[2]);
            Assert.Equal(95.0, scoreCol[2]);
        }

        [Fact]
        public void TestJoinWithDifferentKeyNames()
        {
            var left = new DataFrame(
                new DataColumn<int>("UserId", new[] { 10, 20 }),
                new DataColumn<string>("Role", new[] { "Admin", "Operator" })
            );

            var right = new DataFrame(
                new DataColumn<int>("EmpId", new[] { 20, 30 }),
                new DataColumn<string>("Dept", new[] { "Manufacturing", "Quality" })
            );

            var joined = left.Join(right, "UserId", "EmpId", JoinType.Inner);

            Assert.Equal(1, joined.RowCount);
            Assert.Equal(4, joined.ColumnCount); // UserId, Role, EmpId, Dept

            var uidCol = (DataColumn<int>)joined["UserId"];
            var empIdCol = (DataColumn<int>)joined["EmpId"];
            var deptCol = (DataColumn<string>)joined["Dept"];

            Assert.Equal(20, uidCol[0]);
            Assert.Equal(20, empIdCol[0]);
            Assert.Equal("Manufacturing", deptCol[0]);
        }
    }
}
