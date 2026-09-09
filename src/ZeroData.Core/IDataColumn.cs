using System;

namespace ZeroData.Core
{
    /// <summary>
    /// Represents a strongly-typed contiguous in-memory columnar data vector.
    /// Eliminates row-based object wrapping and GC fragmentation.
    /// </summary>
    public interface IDataColumn
    {
        /// <summary>
        /// Gets the name of the column.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the scalar type stored in this column.
        /// </summary>
        Type DataType { get; }

        /// <summary>
        /// Gets the total number of elements in the column.
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Gets the untyped value at the specified row index.
        /// </summary>
        object? GetValue(int index);

        /// <summary>
        /// Sets the untyped value at the specified row index.
        /// </summary>
        void SetValue(int index, object? value);

        /// <summary>
        /// Creates a sliced contiguous view or copy over the range [start, start + length).
        /// </summary>
        IDataColumn Slice(int start, int length);

        /// <summary>
        /// Creates an independent clone of this column.
        /// </summary>
        IDataColumn Clone();

        /// <summary>
        /// Filters column rows using the provided array of row indices.
        /// </summary>
        IDataColumn Filter(int[] indices);
    }
}
