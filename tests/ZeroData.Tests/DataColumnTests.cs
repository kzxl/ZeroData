using System;
using Xunit;
using ZeroData.Core;

namespace ZeroData.Tests
{
    public class DataColumnTests
    {
        [Fact]
        public void DataColumn_ContiguousAccessAndSlicing_WorkAccurately()
        {
            var col = new DataColumn<double>("Pressure", new[] { 101.3, 102.5, 99.8, 104.1 });
            Assert.Equal(4, col.Length);
            Assert.Equal("Pressure", col.Name);
            Assert.Equal(typeof(double), col.DataType);

            Assert.Equal(101.3, col[0]);
            Assert.Equal(104.1, col[3]);

            // Slice [1, 2) -> 102.5, 99.8
            var slice = (DataColumn<double>)col.Slice(1, 2);
            Assert.Equal(2, slice.Length);
            Assert.Equal(102.5, slice[0]);
            Assert.Equal(99.8, slice[1]);
        }

        [Fact]
        public void DataColumn_Aggregations_AreExact()
        {
            var col = new DataColumn<double>("Voltage", new[] { 10.0, 20.0, 30.0, 40.0 });

            Assert.Equal(100.0, col.SumAsDouble());
            Assert.Equal(25.0, col.MeanAsDouble());
            Assert.Equal(10.0, col.MinAsDouble());
            Assert.Equal(40.0, col.MaxAsDouble());
        }

        [Fact]
        public void DataColumn_Filter_ExtractsTargetRowIndices()
        {
            var col = new DataColumn<string>("Device", new[] { "SensorA", "SensorB", "SensorA", "SensorC" });
            var filtered = (DataColumn<string>)col.Filter(new[] { 0, 2 });

            Assert.Equal(2, filtered.Length);
            Assert.Equal("SensorA", filtered[0]);
            Assert.Equal("SensorA", filtered[1]);
        }
    }
}
