using System;
using System.Collections.Generic;

namespace ZeroData.Core
{
    /// <summary>
    /// Supported relational join types between DataFrames.
    /// </summary>
    public enum JoinType
    {
        Inner,
        Left,
        Right,
        FullOuter
    }

    public partial class DataFrame
    {
        /// <summary>
        /// Performs a relational hash join with another DataFrame on a common key column.
        /// </summary>
        public DataFrame Join(
            DataFrame other,
            string onKey,
            JoinType joinType = JoinType.Inner,
            string leftSuffix = "",
            string rightSuffix = "_right")
        {
            return Join(other, onKey, onKey, joinType, leftSuffix, rightSuffix);
        }

        /// <summary>
        /// Performs a relational hash join with another DataFrame matching leftKey to rightKey.
        /// </summary>
        public DataFrame Join(
            DataFrame other,
            string leftKey,
            string rightKey,
            JoinType joinType = JoinType.Inner,
            string leftSuffix = "",
            string rightSuffix = "_right")
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (string.IsNullOrEmpty(leftKey)) throw new ArgumentNullException(nameof(leftKey));
            if (string.IsNullOrEmpty(rightKey)) throw new ArgumentNullException(nameof(rightKey));

            var lCol = GetColumn(leftKey);
            var rCol = other.GetColumn(rightKey);

            // 1. Build Phase: Build hash index on right table key
            var rightMap = new Dictionary<object, List<int>>();
            for (int r = 0; r < other.RowCount; r++)
            {
                var val = rCol.GetValue(r);
                if (val == null) continue;

                if (!rightMap.TryGetValue(val, out var list))
                {
                    list = new List<int>();
                    rightMap[val] = list;
                }
                list.Add(r);
            }

            // 2. Probe Phase: Walk left table and match
            var leftIndices = new List<int>();
            var rightIndices = new List<int>();
            var rightMatched = (joinType == JoinType.Right || joinType == JoinType.FullOuter) ? new bool[other.RowCount] : null;

            for (int l = 0; l < RowCount; l++)
            {
                var val = lCol.GetValue(l);
                bool matched = false;

                if (val != null && rightMap.TryGetValue(val, out var matches))
                {
                    matched = true;
                    for (int m = 0; m < matches.Count; m++)
                    {
                        int rIdx = matches[m];
                        leftIndices.Add(l);
                        rightIndices.Add(rIdx);
                        if (rightMatched != null) rightMatched[rIdx] = true;
                    }
                }

                if (!matched && (joinType == JoinType.Left || joinType == JoinType.FullOuter))
                {
                    leftIndices.Add(l);
                    rightIndices.Add(-1); // Null right row
                }
            }

            // 3. Unmatched Right Rows for Right and FullOuter Joins
            if (rightMatched != null)
            {
                for (int r = 0; r < other.RowCount; r++)
                {
                    if (!rightMatched[r])
                    {
                        leftIndices.Add(-1); // Null left row
                        rightIndices.Add(r);
                    }
                }
            }

            // 4. Materialization Phase: Build Resulting Columns
            var resultCols = new List<IDataColumn>();

            // Add left columns
            for (int i = 0; i < ColumnCount; i++)
            {
                string colName = _columnOrder[i];
                var srcCol = _columns[colName];

                string finalName = colName;
                if (other._columns.ContainsKey(colName) && colName != leftKey)
                {
                    finalName = colName + leftSuffix;
                }

                resultCols.Add(GatherColumn(srcCol, finalName, leftIndices));
            }

            // Add right columns
            for (int i = 0; i < other.ColumnCount; i++)
            {
                string rColName = other._columnOrder[i];
                // If same key name used for join, omit duplicate key column in output
                if (string.Equals(rColName, rightKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var srcCol = other._columns[rColName];
                string finalName = rColName;
                if (_columns.ContainsKey(rColName))
                {
                    finalName = rColName + rightSuffix;
                }

                resultCols.Add(GatherColumn(srcCol, finalName, rightIndices));
            }

            return new DataFrame(resultCols.ToArray());
        }

        private static IDataColumn GatherColumn(IDataColumn src, string name, List<int> indices)
        {
            if (src is DataColumn<int> cInt)
            {
                var data = new int[indices.Count];
                var span = cInt.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<int>(name, data);
            }
            if (src is DataColumn<double> cDbl)
            {
                var data = new double[indices.Count];
                var span = cDbl.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<double>(name, data);
            }
            if (src is DataColumn<float> cFlt)
            {
                var data = new float[indices.Count];
                var span = cFlt.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<float>(name, data);
            }
            if (src is DataColumn<long> cLng)
            {
                var data = new long[indices.Count];
                var span = cLng.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<long>(name, data);
            }
            if (src is DataColumn<string> cStr)
            {
                var data = new string?[indices.Count];
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? (string?)cStr.GetValue(idx) : null;
                }
                return new DataColumn<string>(name, data!);
            }
            if (src is DataColumn<DateTime> cDt)
            {
                var data = new DateTime[indices.Count];
                var span = cDt.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<DateTime>(name, data);
            }
            if (src is DataColumn<bool> cBool)
            {
                var data = new bool[indices.Count];
                var span = cBool.AsReadOnlySpan();
                for (int i = 0; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    data[i] = idx >= 0 ? span[idx] : default;
                }
                return new DataColumn<bool>(name, data);
            }

            // Fallback for any other type
            Type colType = typeof(DataColumn<>).MakeGenericType(src.DataType);
            Array arr = Array.CreateInstance(src.DataType, indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                if (idx >= 0)
                {
                    arr.SetValue(src.GetValue(idx), i);
                }
            }
            return (IDataColumn)Activator.CreateInstance(colType, name, arr)!;
        }
    }
}
