using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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
    public class DataFrame
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
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(delimiter.ToString(), _columnOrder));

            for (int r = 0; r < _rowCount; r++)
            {
                for (int c = 0; c < _columnOrder.Count; c++)
                {
                    if (c > 0) sb.Append(delimiter);
                    var val = _columns[_columnOrder[c]].GetValue(r);
                    if (val is string s && (s.Contains(delimiter) || s.Contains('"') || s.Contains('\n')))
                    {
                        sb.Append($"\"{s.Replace("\"", "\"\"")}\"");
                    }
                    else
                    {
                        sb.Append(Convert.ToString(val, CultureInfo.InvariantCulture));
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static DataFrame FromCsv(string csvContent, char delimiter = ',')
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return new DataFrame();

            using var reader = new StringReader(csvContent);
            string? headerLine = reader.ReadLine();
            if (headerLine == null) return new DataFrame();

            var headers = ParseCsvLine(headerLine, delimiter);
            var rawColumns = new List<string>[headers.Count];
            for (int i = 0; i < headers.Count; i++) rawColumns[i] = new List<string>();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = ParseCsvLine(line, delimiter);
                for (int i = 0; i < headers.Count; i++)
                {
                    rawColumns[i].Add(i < parts.Count ? parts[i] : "");
                }
            }

            var df = new DataFrame();
            for (int c = 0; c < headers.Count; c++)
            {
                var strings = rawColumns[c];
                // Infer type: double, int, or string
                if (strings.Count > 0 && strings.All(s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
                {
                    var doubles = strings.Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    df.AddColumn(new DataColumn<double>(headers[c], doubles));
                }
                else
                {
                    df.AddColumn(new DataColumn<string>(headers[c], strings.ToArray()));
                }
            }

            return df;
        }

        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString().Trim());
            return result;
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
