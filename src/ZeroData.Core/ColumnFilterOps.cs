using System;
using System.Collections.Generic;
using ZeroPrimitives.Simd;

namespace ZeroData.Core
{
    /// <summary>
    /// Comparison operators for columnar filtering.
    /// </summary>
    public enum FilterOp : byte
    {
        Equal = 0,
        NotEqual = 1,
        GreaterThan = 2,
        GreaterThanOrEqual = 3,
        LessThan = 4,
        LessThanOrEqual = 5
    }

    /// <summary>
    /// Hardware-accelerated vectorized columnar filter engine for DataColumns.
    /// Connects DataColumns with ZeroPrimitives SIMD registers for ultra-low latency analytical queries.
    /// </summary>
    public static class ColumnFilterOps
    {
        #region Matching Indices

        /// <summary>
        /// Finds all row indices where the column values satisfy <c>col[i] op threshold</c>.
        /// Automatically skips and invalidates null rows via Apache Arrow null bitmasks.
        /// </summary>
        public static int[] GetMatchingIndices<T>(this DataColumn<T> col, FilterOp op, T threshold)
        {
            if (col == null) throw new ArgumentNullException(nameof(col));
            int count = col.Length;
            if (count == 0) return Array.Empty<int>();

            // Fast SIMD Path for primitives
            if (typeof(T) == typeof(double))
            {
                var span = (col.RawData as double[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectIndices(span, (VectorCompareOp)op, (double)(object)threshold!, buffer);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                // Filter out nulls
                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(float))
            {
                var span = (col.RawData as float[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectIndices(span, (VectorCompareOp)op, (float)(object)threshold!, buffer);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(int))
            {
                var span = (col.RawData as int[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectIndices(span, (VectorCompareOp)op, (int)(object)threshold!, buffer);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(long))
            {
                var span = (col.RawData as long[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectIndices(span, (VectorCompareOp)op, (long)(object)threshold!, buffer);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            // General IComparable / Equality fallback
            var comparer = Comparer<T>.Default;
            var equality = EqualityComparer<T>.Default;
            var list = new List<int>();

            for (int i = 0; i < count; i++)
            {
                if (col.HasNulls && col.IsNull(i)) continue;

                T val = col[i];
                bool ok = false;
                switch (op)
                {
                    case FilterOp.Equal:
                        ok = equality.Equals(val, threshold);
                        break;
                    case FilterOp.NotEqual:
                        ok = !equality.Equals(val, threshold);
                        break;
                    case FilterOp.GreaterThan:
                        ok = comparer.Compare(val, threshold) > 0;
                        break;
                    case FilterOp.GreaterThanOrEqual:
                        ok = comparer.Compare(val, threshold) >= 0;
                        break;
                    case FilterOp.LessThan:
                        ok = comparer.Compare(val, threshold) < 0;
                        break;
                    case FilterOp.LessThanOrEqual:
                        ok = comparer.Compare(val, threshold) <= 0;
                        break;
                }

                if (ok) list.Add(i);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Finds all row indices where the column values fall within <c>[low, high]</c>.
        /// </summary>
        public static int[] GetMatchingIndicesBetween<T>(this DataColumn<T> col, T low, T high, bool inclusive = true)
        {
            if (col == null) throw new ArgumentNullException(nameof(col));
            int count = col.Length;
            if (count == 0) return Array.Empty<int>();

            if (typeof(T) == typeof(double))
            {
                var span = (col.RawData as double[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectBetween(span, (double)(object)low!, (double)(object)high!, buffer, inclusive);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(float))
            {
                var span = (col.RawData as float[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectBetween(span, (float)(object)low!, (float)(object)high!, buffer, inclusive);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(int))
            {
                var span = (col.RawData as int[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectBetween(span, (int)(object)low!, (int)(object)high!, buffer, inclusive);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            if (typeof(T) == typeof(long))
            {
                var span = (col.RawData as long[]).AsSpan(0, count);
                int[] buffer = new int[count];
                int matched = SimdComparison.SelectBetween(span, (long)(object)low!, (long)(object)high!, buffer, inclusive);

                if (!col.HasNulls)
                {
                    if (matched == count) return buffer;
                    var res = new int[matched];
                    Array.Copy(buffer, res, matched);
                    return res;
                }

                int validCount = 0;
                for (int i = 0; i < matched; i++)
                {
                    if (!col.IsNull(buffer[i]))
                    {
                        buffer[validCount++] = buffer[i];
                    }
                }
                var filtered = new int[validCount];
                Array.Copy(buffer, filtered, validCount);
                return filtered;
            }

            var comparer = Comparer<T>.Default;
            var list = new List<int>();

            for (int i = 0; i < count; i++)
            {
                if (col.HasNulls && col.IsNull(i)) continue;

                T val = col[i];
                int cmpLow = comparer.Compare(val, low);
                int cmpHigh = comparer.Compare(val, high);
                bool ok = inclusive ? (cmpLow >= 0 && cmpHigh <= 0) : (cmpLow > 0 && cmpHigh < 0);

                if (ok) list.Add(i);
            }

            return list.ToArray();
        }

        #endregion

        #region Mask Creation & Direct Filtering

        /// <summary>
        /// Creates a SelectionMask representing the rows satisfying the comparison.
        /// </summary>
        public static SelectionMask CreateMask<T>(this DataColumn<T> col, FilterOp op, T threshold)
        {
            var indices = col.GetMatchingIndices(op, threshold);
            return SelectionMask.FromIndices(col.Length, indices);
        }

        /// <summary>
        /// Creates a SelectionMask representing the rows satisfying the range condition.
        /// </summary>
        public static SelectionMask CreateMaskBetween<T>(this DataColumn<T> col, T low, T high, bool inclusive = true)
        {
            var indices = col.GetMatchingIndicesBetween(low, high, inclusive);
            return SelectionMask.FromIndices(col.Length, indices);
        }

        /// <summary>
        /// Filters this DataColumn using vectorized SIMD evaluation.
        /// </summary>
        public static DataColumn<T> Filter<T>(this DataColumn<T> col, FilterOp op, T threshold)
        {
            var indices = col.GetMatchingIndices(op, threshold);
            return (DataColumn<T>)col.Filter(indices);
        }

        /// <summary>
        /// Filters this DataColumn within the specified range using vectorized SIMD evaluation.
        /// </summary>
        public static DataColumn<T> FilterBetween<T>(this DataColumn<T> col, T low, T high, bool inclusive = true)
        {
            var indices = col.GetMatchingIndicesBetween(low, high, inclusive);
            return (DataColumn<T>)col.Filter(indices);
        }

        #endregion

        #region Vector Statistical Aggregations

        /// <summary>
        /// Computes minimum and maximum elements in a single vectorized pass.
        /// </summary>
        public static void SimdMinMax(this DataColumn<double> col, out double min, out double max)
        {
            if (col == null) throw new ArgumentNullException(nameof(col));
            if (col.Length == 0)
            {
                min = double.NaN;
                max = double.NaN;
                return;
            }

            if (!col.HasNulls)
            {
                SimdAggregations.MinMax(col.RawData.AsSpan(0, col.Length), out min, out max);
                return;
            }

            double localMin = double.PositiveInfinity;
            double localMax = double.NegativeInfinity;
            bool any = false;
            for (int i = 0; i < col.Length; i++)
            {
                if (!col.IsNull(i))
                {
                    double v = col[i];
                    if (v < localMin) localMin = v;
                    if (v > localMax) localMax = v;
                    any = true;
                }
            }

            min = any ? localMin : double.NaN;
            max = any ? localMax : double.NaN;
        }

        /// <summary>
        /// Computes minimum and maximum elements in a single vectorized pass.
        /// </summary>
        public static void SimdMinMax(this DataColumn<float> col, out float min, out float max)
        {
            if (col == null) throw new ArgumentNullException(nameof(col));
            if (col.Length == 0)
            {
                min = float.NaN;
                max = float.NaN;
                return;
            }

            if (!col.HasNulls)
            {
                SimdAggregations.MinMax(col.RawData.AsSpan(0, col.Length), out min, out max);
                return;
            }

            float localMin = float.PositiveInfinity;
            float localMax = float.NegativeInfinity;
            bool any = false;
            for (int i = 0; i < col.Length; i++)
            {
                if (!col.IsNull(i))
                {
                    float v = col[i];
                    if (v < localMin) localMin = v;
                    if (v > localMax) localMax = v;
                    any = true;
                }
            }

            min = any ? localMin : float.NaN;
            max = any ? localMax : float.NaN;
        }

        /// <summary>
        /// Computes arithmetic mean and sample variance using two-pass SIMD vectorization.
        /// </summary>
        public static void SimdVariance(this DataColumn<double> col, out double mean, out double variance)
        {
            if (col == null) throw new ArgumentNullException(nameof(col));
            if (!col.HasNulls)
            {
                SimdAggregations.Variance(col.RawData.AsSpan(0, col.Length), out mean, out variance);
                return;
            }

            // Fallback for nulls
            mean = col.MeanAsDouble();
            double sumSq = 0.0;
            int count = 0;
            for (int i = 0; i < col.Length; i++)
            {
                if (!col.IsNull(i))
                {
                    double diff = col[i] - mean;
                    sumSq += diff * diff;
                    count++;
                }
            }
            variance = count > 1 ? sumSq / (count - 1) : 0.0;
        }

        #endregion
    }
}
