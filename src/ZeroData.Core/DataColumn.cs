using System;
using System.Collections.Generic;

namespace ZeroData.Core
{
    /// <summary>
    /// Contiguous in-memory column storing elements of type T without per-row object allocations.
    /// </summary>
    /// <typeparam name="T">Element type (e.g. double, float, int, long, DateTime, string, bool).</typeparam>
    public class DataColumn<T> : IDataColumn
    {
        private readonly string _name;
        private T[] _data;
        private int _length;

        public string Name => _name;
        public Type DataType => typeof(T);
        public int Length => _length;

        public DataColumn(string name, int initialCapacity = 0)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _data = new T[initialCapacity];
            _length = initialCapacity;
        }

        public DataColumn(string name, T[] data)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _length = data.Length;
        }

        public DataColumn(string name, IEnumerable<T> values)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            var list = new List<T>(values);
            _data = list.ToArray();
            _length = _data.Length;
        }

        public ref T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length) throw new ArgumentOutOfRangeException(nameof(index));
                return ref _data[index];
            }
        }

        public Span<T> AsSpan() => new Span<T>(_data, 0, _length);

        public ReadOnlySpan<T> AsReadOnlySpan() => new ReadOnlySpan<T>(_data, 0, _length);

        public object? GetValue(int index)
        {
            if ((uint)index >= (uint)_length) throw new ArgumentOutOfRangeException(nameof(index));
            return _data[index];
        }

        public void SetValue(int index, object? value)
        {
            if ((uint)index >= (uint)_length) throw new ArgumentOutOfRangeException(nameof(index));
            _data[index] = value == null ? default! : (T)Convert.ChangeType(value, typeof(T));
        }

        public IDataColumn Slice(int start, int length)
        {
            if (start < 0 || length < 0 || start + length > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "Invalid slice range.");
            }

            var sliceData = new T[length];
            Array.Copy(_data, start, sliceData, 0, length);
            return new DataColumn<T>(_name, sliceData);
        }

        public IDataColumn Clone()
        {
            var cloneData = new T[_length];
            Array.Copy(_data, cloneData, _length);
            return new DataColumn<T>(_name, cloneData);
        }

        public IDataColumn Filter(int[] indices)
        {
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            var filtered = new T[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                filtered[i] = _data[indices[i]];
            }
            return new DataColumn<T>(_name, filtered);
        }

        public T[] ToArray()
        {
            var copy = new T[_length];
            Array.Copy(_data, copy, _length);
            return copy;
        }

        #region Numeric Aggregations

        public double SumAsDouble()
        {
            double sum = 0.0;
            if (typeof(T) == typeof(double))
            {
                var arr = (double[])(object)_data;
                for (int i = 0; i < _length; i++) sum += arr[i];
            }
            else if (typeof(T) == typeof(float))
            {
                var arr = (float[])(object)_data;
                for (int i = 0; i < _length; i++) sum += arr[i];
            }
            else if (typeof(T) == typeof(int))
            {
                var arr = (int[])(object)_data;
                for (int i = 0; i < _length; i++) sum += arr[i];
            }
            else if (typeof(T) == typeof(long))
            {
                var arr = (long[])(object)_data;
                for (int i = 0; i < _length; i++) sum += arr[i];
            }
            else
            {
                throw new NotSupportedException($"Sum is not supported for type {typeof(T)}.");
            }

            return sum;
        }

        public double MeanAsDouble()
        {
            if (_length == 0) return 0.0;
            return SumAsDouble() / _length;
        }

        public double MinAsDouble()
        {
            if (_length == 0) return double.NaN;
            double min = double.PositiveInfinity;

            for (int i = 0; i < _length; i++)
            {
                double val = Convert.ToDouble(_data[i]);
                if (val < min) min = val;
            }

            return min;
        }

        public double MaxAsDouble()
        {
            if (_length == 0) return double.NaN;
            double max = double.NegativeInfinity;

            for (int i = 0; i < _length; i++)
            {
                double val = Convert.ToDouble(_data[i]);
                if (val > max) max = val;
            }

            return max;
        }

        #endregion
    }
}
