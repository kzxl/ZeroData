using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ZeroData.Sql.Execution;

namespace ZeroData.Sql
{
    /// <summary>
    /// Ultra-high-performance native ADO.NET query execution and materialization engine.
    /// Drop-in sovereign replacement for Dapper in ZeroPlatform.
    /// Zero external dependencies, thread-safe, and fully async.
    /// </summary>
    public static class ZeroSqlExecutor
    {
        #region Command Setup Helper

        private static IDbCommand PrepareCommand(
            IDbConnection connection,
            string sql,
            object parameters,
            IDbTransaction transaction,
            int? commandTimeout,
            CommandType? commandType,
            ValueConverterCollection converters)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            if (transaction != null)
                cmd.Transaction = transaction;

            if (commandTimeout.HasValue)
                cmd.CommandTimeout = commandTimeout.Value;

            if (commandType.HasValue)
                cmd.CommandType = commandType.Value;

            if (parameters != null)
            {
                FastParameterBinder.Bind(cmd, parameters, converters);
            }

            return cmd;
        }

        #endregion

        #region Synchronous Query Methods

        /// <summary>
        /// Executes a query and materializes each row into an instance of T.
        /// </summary>
        public static IEnumerable<T> Query<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed) connection.Open();

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                using (var reader = cmd.ExecuteReader())
                {
                    var results = new List<T>();
                    while (reader.Read())
                    {
                        results.Add(EntityMaterializer.Materialize<T>(reader, converters));
                    }
                    return results;
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Executes a query returning untyped dynamic rows (ZeroRow).
        /// </summary>
        public static IEnumerable<dynamic> Query(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            return connection.Query<dynamic>(sql, param, transaction, commandTimeout, commandType, converters);
        }

        /// <summary>
        /// Executes a query and materializes each row into the given Type dynamically.
        /// </summary>
        public static IEnumerable<object> Query(
            this IDbConnection connection,
            Type type,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed) connection.Open();

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                using (var reader = cmd.ExecuteReader())
                {
                    var results = new List<object>();
                    while (reader.Read())
                    {
                        results.Add(EntityMaterializer.Materialize(reader, type, converters));
                    }
                    return results;
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Executes a query and returns the first row materialized to T, or default(T) if no rows found.
        /// </summary>
        public static T QueryFirstOrDefault<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed) connection.Open();

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                {
                    if (reader.Read())
                    {
                        return EntityMaterializer.Materialize<T>(reader, converters);
                    }
                    return default;
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        #endregion

        #region Asynchronous Query Methods

        /// <summary>
        /// Asynchronously executes a query and materializes each row into an instance of T.
        /// </summary>
        public static async Task<IEnumerable<T>> QueryAsync<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                if (connection is DbConnection dbConn)
                    await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                else
                    connection.Open();
            }

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    if (cmd is DbCommand dbCmd)
                    {
                        using (var reader = await dbCmd.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                        {
                            var results = new List<T>();
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                results.Add(EntityMaterializer.Materialize<T>(reader, converters));
                            }
                            return results;
                        }
                    }
                    else
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            var results = new List<T>();
                            while (reader.Read())
                            {
                                results.Add(EntityMaterializer.Materialize<T>(reader, converters));
                            }
                            return results;
                        }
                    }
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Asynchronously executes a query returning untyped dynamic rows (ZeroRow).
        /// </summary>
        public static Task<IEnumerable<dynamic>> QueryAsync(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            return connection.QueryAsync<dynamic>(sql, param, transaction, commandTimeout, commandType, cancellationToken, converters);
        }

        /// <summary>
        /// Asynchronously executes a query and materializes each row into the given Type dynamically.
        /// </summary>
        public static async Task<IEnumerable<object>> QueryAsync(
            this IDbConnection connection,
            Type type,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                if (connection is DbConnection dbConn)
                    await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                else
                    connection.Open();
            }

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    if (cmd is DbCommand dbCmd)
                    {
                        using (var reader = await dbCmd.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                        {
                            var results = new List<object>();
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                results.Add(EntityMaterializer.Materialize(reader, type, converters));
                            }
                            return results;
                        }
                    }
                    else
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            var results = new List<object>();
                            while (reader.Read())
                            {
                                results.Add(EntityMaterializer.Materialize(reader, type, converters));
                            }
                            return results;
                        }
                    }
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Asynchronously executes a query and returns the first row materialized to T, or default(T) if no rows found.
        /// </summary>
        public static async Task<T> QueryFirstOrDefaultAsync<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                if (connection is DbConnection dbConn)
                    await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                else
                    connection.Open();
            }

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    if (cmd is DbCommand dbCmd)
                    {
                        using (var reader = await dbCmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                return EntityMaterializer.Materialize<T>(reader, converters);
                            }
                            return default;
                        }
                    }
                    else
                    {
                        using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            if (reader.Read())
                            {
                                return EntityMaterializer.Materialize<T>(reader, converters);
                            }
                            return default;
                        }
                    }
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        #endregion

        #region Execute & ExecuteScalar Methods

        /// <summary>
        /// Executes a SQL statement, returning the number of affected rows.
        /// </summary>
        public static int Execute(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed) connection.Open();

            try
            {
                if (param is System.Collections.IEnumerable enumerable
                    && !(param is string)
                    && !(param is byte[])
                    && !(param is IDictionary<string, object>)
                    && !(param is System.Collections.IDictionary))
                {
                    int total = 0;
                    foreach (var item in enumerable)
                    {
                        using (var cmd = PrepareCommand(connection, sql, item, transaction, commandTimeout, commandType, converters))
                        {
                            total += cmd.ExecuteNonQuery();
                        }
                    }
                    return total;
                }

                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    return cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Asynchronously executes a SQL statement, returning the number of affected rows.
        /// </summary>
        public static async Task<int> ExecuteAsync(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                if (connection is DbConnection dbConn)
                    await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                else
                    connection.Open();
            }

            try
            {
                if (param is System.Collections.IEnumerable enumerable
                    && !(param is string)
                    && !(param is byte[])
                    && !(param is IDictionary<string, object>)
                    && !(param is System.Collections.IDictionary))
                {
                    int total = 0;
                    foreach (var item in enumerable)
                    {
                        using (var cmd = PrepareCommand(connection, sql, item, transaction, commandTimeout, commandType, converters))
                        {
                            if (cmd is DbCommand dbCmd)
                                total += await dbCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                            else
                                total += cmd.ExecuteNonQuery();
                        }
                    }
                    return total;
                }

                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    if (cmd is DbCommand dbCmd)
                        return await dbCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    return cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Executes a query and returns the first column of the first row converted to T.
        /// </summary>
        public static T ExecuteScalar<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed) connection.Open();

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    var result = cmd.ExecuteScalar();
                    if (result == null || result is DBNull)
                        return default;

                    if (converters != null && converters.HasConverter(typeof(T)))
                    {
                        return (T)converters.ConvertFromDb(result, typeof(T));
                    }

                    return FastConvert.ChangeType<T>(result);
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        /// <summary>
        /// Asynchronously executes a query and returns the first column of the first row converted to T.
        /// </summary>
        public static async Task<T> ExecuteScalarAsync<T>(
            this IDbConnection connection,
            string sql,
            object param = null,
            IDbTransaction transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null,
            CancellationToken cancellationToken = default,
            ValueConverterCollection converters = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                if (connection is DbConnection dbConn)
                    await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                else
                    connection.Open();
            }

            try
            {
                using (var cmd = PrepareCommand(connection, sql, param, transaction, commandTimeout, commandType, converters))
                {
                    object result;
                    if (cmd is DbCommand dbCmd)
                        result = await dbCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    else
                        result = cmd.ExecuteScalar();

                    if (result == null || result is DBNull)
                        return default;

                    if (converters != null && converters.HasConverter(typeof(T)))
                    {
                        return (T)converters.ConvertFromDb(result, typeof(T));
                    }

                    return FastConvert.ChangeType<T>(result);
                }
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }

        #endregion

        #region CommandDefinition Overloads

        public static IEnumerable<T> Query<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.Query<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                converters);
        }

        public static Task<IEnumerable<T>> QueryAsync<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.QueryAsync<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                command.CancellationToken,
                converters);
        }

        public static IEnumerable<object> Query(
            this IDbConnection connection,
            Type type,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.Query(
                type,
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                converters);
        }

        public static Task<IEnumerable<object>> QueryAsync(
            this IDbConnection connection,
            Type type,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.QueryAsync(
                type,
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                command.CancellationToken,
                converters);
        }

        public static T QueryFirstOrDefault<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.QueryFirstOrDefault<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                converters);
        }

        public static Task<T> QueryFirstOrDefaultAsync<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.QueryFirstOrDefaultAsync<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                command.CancellationToken,
                converters);
        }

        public static int Execute(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.Execute(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                converters);
        }

        public static Task<int> ExecuteAsync(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.ExecuteAsync(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                command.CancellationToken,
                converters);
        }

        public static T ExecuteScalar<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.ExecuteScalar<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                converters);
        }

        public static Task<T> ExecuteScalarAsync<T>(
            this IDbConnection connection,
            CommandDefinition command,
            ValueConverterCollection converters = null)
        {
            return connection.ExecuteScalarAsync<T>(
                command.CommandText,
                command.Parameters,
                command.Transaction,
                command.CommandTimeout,
                command.CommandType,
                command.CancellationToken,
                converters);
        }

        #endregion
    }
}
