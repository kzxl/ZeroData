using System;
using Xunit;
using ZeroData.Core;
using ZeroData.Core.Arrow;

namespace ZeroData.Tests
{
    public class ArrowIpcTests
    {
        [Fact]
        public void TestArrowIpc_RoundTrip_Primitives()
        {
            var df = new DataFrame(
                new DataColumn<int>("IntCol", new[] { 10, 20, 30, 40 }),
                new DataColumn<double>("DblCol", new[] { 1.5, 2.5, 3.5, 4.5 }),
                new DataColumn<float>("FltCol", new[] { 0.1f, 0.2f, 0.3f, 0.4f }),
                new DataColumn<long>("LngCol", new[] { 100000L, 200000L, 300000L, 400000L }),
                new DataColumn<bool>("BoolCol", new[] { true, false, true, false })
            );

            byte[] ipcBytes = ArrowIpcWriter.Serialize(df);
            Assert.NotEmpty(ipcBytes);

            var roundTrip = ArrowIpcReader.Deserialize(ipcBytes);

            Assert.Equal(df.RowCount, roundTrip.RowCount);
            Assert.Equal(df.ColumnCount, roundTrip.ColumnCount);

            var intCol = (DataColumn<int>)roundTrip["IntCol"];
            var dblCol = (DataColumn<double>)roundTrip["DblCol"];
            var fltCol = (DataColumn<float>)roundTrip["FltCol"];
            var lngCol = (DataColumn<long>)roundTrip["LngCol"];
            var boolCol = (DataColumn<bool>)roundTrip["BoolCol"];

            for (int i = 0; i < df.RowCount; i++)
            {
                Assert.Equal(((DataColumn<int>)df["IntCol"])[i], intCol[i]);
                Assert.Equal(((DataColumn<double>)df["DblCol"])[i], dblCol[i]);
                Assert.Equal(((DataColumn<float>)df["FltCol"])[i], fltCol[i]);
                Assert.Equal(((DataColumn<long>)df["LngCol"])[i], lngCol[i]);
                Assert.Equal(((DataColumn<bool>)df["BoolCol"])[i], boolCol[i]);
            }
        }

        [Fact]
        public void TestArrowIpc_RoundTrip_StringsAndUnicode()
        {
            var df = new DataFrame(
                new DataColumn<int>("Id", new[] { 1, 2, 3 }),
                new DataColumn<string>("Description", new[] { "Sensor Alpha", "Cảm biến nhiệt độ", "高速カメラ" })
            );

            byte[] ipcBytes = ArrowIpcWriter.Serialize(df);
            var roundTrip = ArrowIpcReader.Deserialize(ipcBytes);

            Assert.Equal(3, roundTrip.RowCount);
            var descCol = (DataColumn<string>)roundTrip["Description"];

            Assert.Equal("Sensor Alpha", descCol[0]);
            Assert.Equal("Cảm biến nhiệt độ", descCol[1]);
            Assert.Equal("高速カメラ", descCol[2]);
        }
    }
}
