using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ZeroPrimitives;
using ZeroPrimitives.Parsing;
using ZeroPrimitives.Text;

namespace ZeroData.Core
{
    public enum ResampleAgg
    {
        Mean,
        Sum,
        Min,
        Max,
        First,
        Last,
        Count
    }

    /// <summary>
    /// High-throughput in-memory Columnar DataFrame.
    /// Stores data in contiguous typed column vectors for zero GC pause analytics, time-series resampling, and fast UI binding.
    /// </summary>
    public partial class DataFrame
    {
        private readonly Dictionary<string, IDataColumn> _columns = new Dictionary<string, IDataColumn>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _columnOrder = new List<string>();
        private int _rowCount;

        public int RowCount => _rowCount;
        public int ColumnCount => _columnOrder.Count;
        public IReadOnlyList<string> ColumnNames => _columnOrder;

        public IDataColumn this[string columnName] => GetColumn(columnName);

        public DataFrame()
        {
        }

        public DataFrame(params IDataColumn[] columns)
        {
            if (columns != null)
            {
                foreach (var col in columns)
                {
                    AddColumn(col);
                }
            }
        }

        public void AddColumn(IDataColumn column)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (_columns.Count == 0)
            {
                _rowCount = column.Length;
            }
            else if (column.Length != _rowCount)
            {
                throw new ArgumentException($"Column '{column.Name}' has length {column.Length}, expected {_rowCount}.", nameof(column));
            }

            if (_columns.ContainsKey(column.Name))
            {
                throw new ArgumentException($"Column '{column.Name}' already exists.", nameof(column));
            }

            _columns[column.Name] = column;
            _columnOrder.Add(column.Name);
        }

        public IDataColumn GetColumn(string name)
        {
            if (!_columns.TryGetValue(name, out var col))
            {
                throw new KeyNotFoundException($"Column '{name}' was not found in DataFrame.");
            }
            return col;
        }

        public DataColumn<T> Column<T>(string name)
        {
            var col = GetColumn(name);
            if (col is DataColumn<T> typedCol)
            {
                return typedCol;
            }

            throw new InvalidCastException($"Column '{name}' is of type {col.DataType.Name}, not {typeof(T).Name}.");
        }

        public bool HasColumn(string name) => _columns.ContainsKey(name);

        public IDataColumn GetColumnByIndex(int index)
        {
            if ((uint)index >= (uint)_columnOrder.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _columns[_columnOrder[index]];
        }

        public RowEnumerable Rows => new RowEnumerable(this);

        #region Slicing & Filtering

        public DataFrame Slice(int start, int length)
        {
            var df = new DataFrame();
            foreach (var colName in _columnOrder)
            {
                df.AddColumn(_columns[colName].Slice(start, length));
            }
            return df;
        }

        public DataFrame Filter(int[] indices)
        {
            var df = new DataFrame();
            foreach (var colName in _columnOrder)
            {
                df.AddColumn(_columns[colName].Filter(indices));
            }
            return df;
        }

        public DataFrame Where<T>(string columnName, Func<T, bool> predicate)
        {
            var col = Column<T>(columnName);
            var matching = new List<int>();

            for (int i = 0; i < col.Length; i++)
            {
                if (predicate(col[i]))
                {
                    matching.Add(i);
                }
            }

            return Filter(matching.ToArray());
        }

        public DataFrame OrderBy<TKey>(string columnName, bool ascending = true) where TKey : IComparable<TKey>
        {
            var col = Column<TKey>(columnName);
            var indices = Enumerable.Range(0, _rowCount).ToArray();

            Array.Sort(indices, (a, b) =>
            {
                int cmp = col[a].CompareTo(col[b]);
                return ascending ? cmp : -cmp;
            });

            return Filter(indices);
        }

        #endregion

        #region GroupBy & Aggregations

        public GroupByResult GroupBy<TKey>(string keyColumn) where TKey : notnull
        {
            var col = Column<TKey>(keyColumn);
            var groups = new Dictionary<TKey, List<int>>();

            for (int i = 0; i < col.Length; i++)
            {
                var key = col[i];
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    groups[key] = list;
                }
                list.Add(i);
            }

            return new GroupByResult(this, keyColumn, groups.ToDictionary(k => (object)k.Key, v => v.Value));
        }

        #endregion

        #region Time-Series Resampling

        /// <summary>
        /// Downsamples a time-series column into fixed time intervals with aggregation.
        /// </summary>
        public DataFrame Resample(
            string timeColumn,
            TimeSpan interval,
            string valueColumn,
            ResampleAgg agg = ResampleAgg.Mean)
        {
            var timeCol = Column<DateTime>(timeColumn);
            var valCol = GetColumn(valueColumn);

            long intervalTicks = interval.Ticks;
            if (intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(interval));

            // Group into time buckets
            var buckets = new SortedDictionary<long, List<int>>();

            for (int i = 0; i < timeCol.Length; i++)
            {
                long ticks = timeCol[i].Ticks;
                long bucketTick = (ticks / intervalTicks) * intervalTicks;

                if (!buckets.TryGetValue(bucketTick, out var list))
                {
                    list = new List<int>();
                    buckets[bucketTick] = list;
                }
                list.Add(i);
            }

            var resTimes = new DateTime[buckets.Count];
            var resValues = new double[buckets.Count];
            int bIdx = 0;

            foreach (var kvp in buckets)
            {
                resTimes[bIdx] = new DateTime(kvp.Key);
                var rowIndices = kvp.Value;

                double aggVal = 0.0;
                if (agg == ResampleAgg.Count)
                {
                    aggVal = rowIndices.Count;
                }
                else if (agg == ResampleAgg.First)
                {
                    aggVal = Convert.ToDouble(valCol.GetValue(rowIndices[0]));
                }
                else if (agg == ResampleAgg.Last)
                {
                    aggVal = Convert.ToDouble(valCol.GetValue(rowIndices[rowIndices.Count - 1]));
                }
                else
                {
                    double sum = 0.0;
                    double min = double.PositiveInfinity;
                    double max = double.NegativeInfinity;

                    for (int i = 0; i < rowIndices.Count; i++)
                    {
                        double v = Convert.ToDouble(valCol.GetValue(rowIndices[i]));
                        sum += v;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }

                    if (agg == ResampleAgg.Mean) aggVal = sum / rowIndices.Count;
                    else if (agg == ResampleAgg.Sum) aggVal = sum;
                    else if (agg == ResampleAgg.Min) aggVal = min;
                    else if (agg == ResampleAgg.Max) aggVal = max;
                }

                resValues[bIdx] = aggVal;
                bIdx++;
            }

            var result = new DataFrame();
            result.AddColumn(new DataColumn<DateTime>(timeColumn, resTimes));
            result.AddColumn(new DataColumn<double>(valueColumn + "_" + agg.ToString().ToLowerInvariant(), resValues));
            return result;
        }

        #endregion

        #region CSV Export & Import

        public string ToCsv(char delimiter = ',')
        {
            var vsb = new ValueStringBuilder(stackalloc char[1024]);
            try
            {
                for (int c = 0; c < _columnOrder.Count; c++)
                {
                    if (c > 0) vsb.Append(delimiter);
                    vsb.Append(_columnOrder[c]);
                }
                vsb.Append("\r\n");

                for (int r = 0; r < _rowCount; r++)
                {
                    for (int c = 0; c < _columnOrder.Count; c++)
                    {
                        if (c > 0) vsb.Append(delimiter);
                        var col = _columns[_columnOrder[c]];
                        if (col.IsNull(r)) continue;

                        var val = col.GetValue(r);
                        if (val is string s)
                        {
                            if (s.IndexOf(delimiter) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                            {
                                vsb.Append('"');
                                for (int i = 0; i < s.Length; i++)
                                {
                                    if (s[i] == '"') vsb.Append('"');
                                    vsb.Append(s[i]);
                                }
                                vsb.Append('"');
                            }
                            else
                            {
                                vsb.Append(s);
                            }
                        }
                        else if (val != null)
                        {
                            vsb.Append(FastConvert.ToStringOrDefault(val));
                        }
                    }
                    vsb.Append("\r\n");
                }

                return vsb.ToString();
            }
            finally
            {
                vsb.Dispose();
            }
        }

        public static DataFrame FromCsv(string csvContent, char delimiter = ',')
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return new DataFrame();

            var rowEnumerator = FastCsvParser.EnumerateRows(csvContent.AsSpan()).GetEnumerator();
            if (!rowEnumerator.MoveNext()) return new DataFrame();

            var firstRow = rowEnumerator.Current;
            var headerList = new List<string>();
            foreach (var cell in FastCsvParser.EnumerateCells(firstRow, delimiter))
            {
                headerList.Add(cell.ToString().Trim());
            }

            if (headerList.Count == 0) return new DataFrame();

            var rawColumns = new List<string>[headerList.Count];
            for (int i = 0; i < headerList.Count; i++) rawColumns[i] = new List<string>();

            char[]? rentedBuf = null;
            Span<char> stackBuf = stackalloc char[512];
            try
            {
                while (rowEnumerator.MoveNext())
                {
                    var rowSpan = rowEnumerator.Current;
                    if (rowSpan.Trim().Length == 0) continue;

                    int colIdx = 0;
                    foreach (var cell in FastCsvParser.EnumerateCells(rowSpan, delimiter))
                    {
                        if (colIdx < headerList.Count)
                        {
                            var trimmedCell = cell.Trim();
                            if (trimmedCell.Length >= 2 && trimmedCell[0] == '"' && trimmedCell[trimmedCell.Length - 1] == '"')
                            {
                                Span<char> destBuf = stackBuf;
                                if (trimmedCell.Length > destBuf.Length)
                                {
                                    if (rentedBuf == null || rentedBuf.Length < trimmedCell.Length)
                                    {
                                        if (rentedBuf != null) System.Buffers.ArrayPool<char>.Shared.Return(rentedBuf);
                                        rentedBuf = System.Buffers.ArrayPool<char>.Shared.Rent(trimmedCell.Length);
                                    }
                                    destBuf = rentedBuf;
                                }
                                var unquoted = FastCsvParser.Unquote(trimmedCell, destBuf, out int written);
                                rawColumns[colIdx].Add(unquoted.ToString());
                            }
                            else
                            {
                                rawColumns[colIdx].Add(trimmedCell.ToString());
                            }
                        }
                        colIdx++;
                    }

                    while (colIdx < headerList.Count)
                    {
                        rawColumns[colIdx].Add(string.Empty);
                        colIdx++;
                    }
                }
            }
            finally
            {
                if (rentedBuf != null) System.Buffers.ArrayPool<char>.Shared.Return(rentedBuf);
            }

            var df = new DataFrame();
            for (int c = 0; c < headerList.Count; c++)
            {
                var strings = rawColumns[c];
                var col = InferAndCreateColumn(headerList[c], strings);
                df.AddColumn(col);
            }

            return df;
        }

        private static IDataColumn InferAndCreateColumn(string name, List<string> strings)
        {
            if (strings.Count == 0) return new DataColumn<string>(name, 0);

            bool isInt = true;
            bool isDouble = true;
            int nonEmptyCount = 0;

            for (int i = 0; i < strings.Count; i++)
            {
                var s = strings[i].Trim();
                if (s.Length == 0) continue;
                nonEmptyCount++;

                if (isInt && !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) isInt = false;
                if (isDouble && !double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _)) isDouble = false;
            }

            if (nonEmptyCount > 0)
            {
                if (isInt)
                {
                    var col = new DataColumn<int>(name, strings.Count);
                    for (int i = 0; i < strings.Count; i++)
                    {
                        var s = strings[i].Trim();
                        if (s.Length == 0) col.SetNull(i);
                        else col.SetValid(i, int.Parse(s, CultureInfo.InvariantCulture));
                    }
                    return col;
                }
                if (isDouble)
                {
                    var col = new DataColumn<double>(name, strings.Count);
                    for (int i = 0; i < strings.Count; i++)
                    {
                        var s = strings[i].Trim();
                        if (s.Length == 0) col.SetNull(i);
                        else col.SetValid(i, double.Parse(s, CultureInfo.InvariantCulture));
                    }
                    return col;
                }
            }

            var strCol = new DataColumn<string>(name, strings.Count);
            for (int i = 0; i < strings.Count; i++)
            {
                strCol.SetValue(i, strings[i]);
            }
            return strCol;
        }

        #endregion

        #region Virtual Mode Grid Adapter

        /// <summary>
        /// Creates a virtual mode provider for zero-overhead binding directly to ZeroUI industrial grids.
        /// </summary>
        public ZeroDataVirtualProvider CreateVirtualProvider() => new ZeroDataVirtualProvider(this);

        #endregion
    }

    /// <summary>
    /// Result of a GroupBy operation supporting multi-column aggregations.
    /// </summary>
    public class GroupByResult
    {
        private readonly DataFrame _source;
        private readonly string _keyColumn;
        private readonly Dictionary<object, List<int>> _groups;

        public int GroupCount => _groups.Count;

        internal GroupByResult(DataFrame source, string keyColumn, Dictionary<object, List<int>> groups)
        {
            _source = source;
            _keyColumn = keyColumn;
            _groups = groups;
        }

        public DataFrame Count()
        {
            var keys = _groups.Keys.ToArray();
            var counts = new int[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                counts[i] = _groups[keys[i]].Count;
            }

            var df = new DataFrame();
            df.AddColumn(CreateKeyColumn(keys));
            df.AddColumn(new DataColumn<int>("Count", counts));
            return df;
        }

        public DataFrame Mean(string numericColumn)
        {
            var keys = _groups.Keys.ToArray();
            var means = new double[keys.Length];
            var valCol = _source.GetColumn(numericColumn);

            for (int i = 0; i < keys.Length; i++)
            {
                var rows = _groups[keys[i]];
                double sum = 0.0;
                for (int r = 0; r < rows.Count; r++)
                {
                    sum += Convert.ToDouble(valCol.GetValue(rows[r]));
                }
                means[i] = sum / rows.Count;
            }

            var df = new DataFrame();
            df.AddColumn(CreateKeyColumn(keys));
            df.AddColumn(new DataColumn<double>(numericColumn + "_mean", means));
            return df;
        }

        public DataFrame Sum(string numericColumn)
        {
            var keys = _groups.Keys.ToArray();
            var sums = new double[keys.Length];
            var valCol = _source.GetColumn(numericColumn);

            for (int i = 0; i < keys.Length; i++)
            {
                var rows = _groups[keys[i]];
                double sum = 0.0;
                for (int r = 0; r < rows.Count; r++)
                {
                    sum += Convert.ToDouble(valCol.GetValue(rows[r]));
                }
                sums[i] = sum;
            }

            var df = new DataFrame();
            df.AddColumn(CreateKeyColumn(keys));
            df.AddColumn(new DataColumn<double>(numericColumn + "_sum", sums));
            return df;
        }

        private IDataColumn CreateKeyColumn(object[] keys)
        {
            var keyColType = _source.GetColumn(_keyColumn).DataType;
            if (keyColType == typeof(string))
            {
                return new DataColumn<string>(_keyColumn, keys.Select(k => (string)k).ToArray());
            }
            if (keyColType == typeof(int))
            {
                return new DataColumn<int>(_keyColumn, keys.Select(k => (int)k).ToArray());
            }
            if (keyColType == typeof(double))
            {
                return new DataColumn<double>(_keyColumn, keys.Select(k => (double)k).ToArray());
            }

            return new DataColumn<string>(_keyColumn, keys.Select(k => k.ToString()!).ToArray());
        }
    }

    /// <summary>
    /// Zero-copy virtual data source feeding 10M+ rows smoothly into ZeroUI grid controls at 60-144 FPS.
    /// </summary>
    public class ZeroDataVirtualProvider
    {
        private readonly DataFrame _df;

        public int RowCount => _df.RowCount;
        public int ColumnCount => _df.ColumnCount;
        public IReadOnlyList<string> ColumnNames => _df.ColumnNames;

        public ZeroDataVirtualProvider(DataFrame df)
        {
            _df = df ?? throw new ArgumentNullException(nameof(df));
        }

        public string GetColumnName(int columnIndex) => _df.ColumnNames[columnIndex];

        public object? GetValue(int rowIndex, int columnIndex)
        {
            return _df[_df.ColumnNames[columnIndex]].GetValue(rowIndex);
        }

        public T GetValue<T>(int rowIndex, string columnName)
        {
            return _df.Column<T>(columnName)[rowIndex];
        }
    }
}
