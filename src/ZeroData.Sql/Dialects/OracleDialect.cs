using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroData.Sql.Dialects
{
    /// <summary>
    /// Oracle database dialect implementation.
    /// Supports Oracle 12c+ (OFFSET/FETCH and IDENTITY syntax).
    /// </summary>
    public class OracleDialect : ISqlDialect
    {
        public string ProviderName => "Oracle";

        public string ParameterPrefix => ":";

        public bool SupportsReturningClause => true;

        public bool SupportsOutputClause => false;

        public bool SupportsMultiRowValues => false;

        /// <summary>
        /// Oracle uses double quotes for case-sensitive identifiers: "TABLE_NAME"
        /// </summary>
        public string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentNullException(nameof(identifier));

            identifier = identifier.Replace("\"", "\"\"");
            return $"\"{identifier}\"";
        }

        /// <summary>
        /// Oracle 12c+ uses standard ANSI OFFSET/FETCH syntax.
        /// </summary>
        public string GetLimitClause(int? skip, int? take)
        {
            if (!skip.HasValue && !take.HasValue)
                return string.Empty;

            var clause = string.Empty;

            if (skip.HasValue && skip.Value > 0)
            {
                clause = $"OFFSET {skip.Value} ROWS";
                if (take.HasValue && take.Value > 0)
                    clause += $" FETCH NEXT {take.Value} ROWS ONLY";
            }
            else if (take.HasValue && take.Value > 0)
            {
                clause = $"OFFSET 0 ROWS FETCH NEXT {take.Value} ROWS ONLY";
            }

            return clause.Trim();
        }

        /// <summary>
        /// Oracle uses RETURNING clause for generated keys.
        /// </summary>
        public string GetLastInsertIdSql(string tableName, string columnName)
        {
            return $"RETURNING {QuoteIdentifier(columnName)} INTO :new_id";
        }

        /// <summary>
        /// Oracle 12c+ uses GENERATED ALWAYS AS IDENTITY.
        /// </summary>
        public string GetAutoIncrementSql()
        {
            return "GENERATED ALWAYS AS IDENTITY";
        }

        public string GetTableExistsSql(string tableName)
        {
            return $"SELECT CASE WHEN EXISTS (SELECT 1 FROM all_tables WHERE UPPER(table_name) = UPPER('{EscapeStringValue(tableName)}')) THEN 1 ELSE 0 END FROM dual";
        }

        public string GetDbType(Type clrType)
        {
            if (clrType == typeof(int)) return "NUMBER(10)";
            if (clrType == typeof(long)) return "NUMBER(19)";
            if (clrType == typeof(short)) return "NUMBER(5)";
            if (clrType == typeof(byte)) return "NUMBER(3)";
            if (clrType == typeof(bool)) return "NUMBER(1)";
            if (clrType == typeof(decimal)) return "NUMBER(18,2)";
            if (clrType == typeof(double)) return "BINARY_DOUBLE";
            if (clrType == typeof(float)) return "BINARY_FLOAT";
            if (clrType == typeof(string)) return "VARCHAR2(4000)";
            if (clrType == typeof(DateTime)) return "TIMESTAMP";
            if (clrType == typeof(DateTimeOffset)) return "TIMESTAMP WITH TIME ZONE";
            if (clrType == typeof(Guid)) return "RAW(16)";
            if (clrType == typeof(byte[])) return "BLOB";

            var underlyingType = Nullable.GetUnderlyingType(clrType);
            if (underlyingType != null)
                return GetDbType(underlyingType);

            return "VARCHAR2(4000)";
        }

        public string EscapeStringValue(string value)
        {
            if (value == null)
                return "NULL";

            return value.Replace("'", "''");
        }

        /// <summary>
        /// Oracle does not support VALUES (...), (...) syntax in pre-23ai versions.
        /// Generates INSERT ALL INTO ... SELECT * FROM dual.
        /// </summary>
        public string GenerateBulkInsertSql(string tableName, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> parameterRows)
        {
            var cols = string.Join(", ", columns);
            var intoClauses = parameterRows.Select(r => $"INTO {tableName} ({cols}) VALUES ({string.Join(", ", r)})");
            return $"INSERT ALL {string.Join(" ", intoClauses)} SELECT * FROM dual";
        }
    }
}
