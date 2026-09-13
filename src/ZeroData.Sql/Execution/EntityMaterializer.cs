using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using ZeroData.Sql.Mapping;

namespace ZeroData.Sql.Execution
{
    /// <summary>
    /// High-performance entity materializer that transforms IDataReader rows into C# objects.
    /// Uses cached Expression Tree compiled delegates for near-native execution speed.
    /// Thread-safe and resilient against schema differences across queries.
    /// </summary>
    public static class EntityMaterializer
    {
        private static readonly ConcurrentDictionary<(Type TargetType, string SchemaKey), Func<IDataReader, ValueConverterCollection, object>> DelegateCache
            = new ConcurrentDictionary<(Type TargetType, string SchemaKey), Func<IDataReader, ValueConverterCollection, object>>();

        /// <summary>
        /// Materializes the current row of the IDataReader into an instance of T.
        /// </summary>
        public static T Materialize<T>(IDataReader reader, ValueConverterCollection converters = null)
        {
            var obj = Materialize(reader, typeof(T), converters);
            if (obj == null)
                return default;
            return (T)obj;
        }

        /// <summary>
        /// Materializes the current row of the IDataReader into an instance of the specified target type.
        /// </summary>
        public static object Materialize(IDataReader reader, Type targetType, ValueConverterCollection converters = null)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            // 1. Primitive / Scalar handling
            if (IsScalarType(targetType))
            {
                if (reader.IsDBNull(0))
                {
                    return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null
                        ? Activator.CreateInstance(targetType)
                        : null;
                }

                var rawValue = reader.GetValue(0);
                if (converters != null && converters.HasConverter(targetType))
                {
                    return converters.ConvertFromDb(rawValue, targetType);
                }
                return FastConvert.ChangeType(rawValue, targetType);
            }

            // 2. Dynamic / ZeroRow handling
            if (targetType == typeof(object) || targetType == typeof(ZeroRow) || targetType == typeof(IDictionary<string, object>))
            {
                var row = new ZeroRow(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[name] = val;
                }
                return row;
            }

            // 3. Strongly-typed POCO entity materialization
            var schemaKey = ComputeSchemaKey(reader);
            var cacheKey = (targetType, schemaKey);

            if (!DelegateCache.TryGetValue(cacheKey, out var materializer))
            {
                materializer = BuildMaterializer(targetType, reader);
                DelegateCache[cacheKey] = materializer;
            }

            return materializer(reader, converters);
        }

        private static bool IsScalarType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive
                || underlying.IsEnum
                || underlying == typeof(string)
                || underlying == typeof(decimal)
                || underlying == typeof(DateTime)
                || underlying == typeof(DateTimeOffset)
                || underlying == typeof(TimeSpan)
                || underlying == typeof(Guid)
                || underlying == typeof(byte[]);
        }

        private static string ComputeSchemaKey(IDataReader reader)
        {
            var sb = new StringBuilder(reader.FieldCount * 16);
            sb.Append(reader.FieldCount).Append(';');
            for (int i = 0; i < reader.FieldCount; i++)
            {
                sb.Append(reader.GetName(i)).Append(':')
                  .Append(reader.GetFieldType(i).Name).Append(';');
            }
            return sb.ToString();
        }

        private static Func<IDataReader, ValueConverterCollection, object> BuildMaterializer(Type targetType, IDataReader reader)
        {
            var ctor = targetType.GetConstructor(Type.EmptyTypes);
            if (ctor == null && targetType.IsValueType)
            {
                return BuildFallbackMaterializer(targetType, reader);
            }

            if (ctor == null)
            {
                throw new InvalidOperationException(
                    $"Type '{targetType.FullName}' must have a parameterless constructor to be materialized from SQL.");
            }

            try
            {
                var readerParam = Expression.Parameter(typeof(IDataReader), "reader");
                var convertersParam = Expression.Parameter(typeof(ValueConverterCollection), "converters");
                var instanceVar = Expression.Variable(targetType, "instance");

                var expressions = new List<Expression>
                {
                    Expression.Assign(instanceVar, Expression.New(ctor))
                };

                EntityMapping mapping = null;
                try
                {
                    mapping = MappingCache.GetMapping(targetType);
                }
                catch
                {
                    // Non-mapped DTO
                }

                var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite)
                    .ToList();

                var isDbNullMethod = typeof(IDataRecord).GetMethod(nameof(IDataRecord.IsDBNull), new[] { typeof(int) });
                var getValueMethod = typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetValue), new[] { typeof(int) });
                var changeTypeMethod = typeof(FastConvert).GetMethod(nameof(FastConvert.ChangeType), new[] { typeof(object), typeof(Type) });
                var convertFromDbMethod = typeof(ValueConverterCollection).GetMethod(nameof(ValueConverterCollection.ConvertFromDb), new[] { typeof(object), typeof(Type) });
                var hasConverterMethod = typeof(ValueConverterCollection).GetMethod(nameof(ValueConverterCollection.HasConverter), new[] { typeof(Type) });

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var colName = reader.GetName(i);
                    PropertyInfo matchedProp = null;

                    if (mapping != null)
                    {
                        var colMapping = mapping.Columns.FirstOrDefault(c =>
                            string.Equals(c.ColumnName, colName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.Property.Name, colName, StringComparison.OrdinalIgnoreCase));

                        if (colMapping != null)
                            matchedProp = colMapping.Property;
                    }

                    if (matchedProp == null)
                    {
                        matchedProp = properties.FirstOrDefault(p =>
                            string.Equals(p.Name, colName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedProp == null || !matchedProp.CanWrite)
                        continue;

                    var indexConst = Expression.Constant(i);
                    var propTypeConst = Expression.Constant(matchedProp.PropertyType);

                    var isDbNullExpr = Expression.Call(readerParam, isDbNullMethod, indexConst);
                    var rawValExpr = Expression.Call(readerParam, getValueMethod, indexConst);

                    var hasConverterCheck = Expression.AndAlso(
                        Expression.NotEqual(convertersParam, Expression.Constant(null, typeof(ValueConverterCollection))),
                        Expression.Call(convertersParam, hasConverterMethod, propTypeConst)
                    );

                    var convertWithConverter = Expression.Call(convertersParam, convertFromDbMethod, rawValExpr, propTypeConst);
                    var convertWithFastConvert = Expression.Call(changeTypeMethod, rawValExpr, propTypeConst);

                    var resolvedObjectExpr = Expression.Condition(hasConverterCheck, convertWithConverter, convertWithFastConvert);
                    var castValueExpr = Expression.Convert(resolvedObjectExpr, matchedProp.PropertyType);

                    var assignPropExpr = Expression.Assign(Expression.Property(instanceVar, matchedProp), castValueExpr);
                    var conditionExpr = Expression.IfThen(Expression.Not(isDbNullExpr), assignPropExpr);

                    expressions.Add(conditionExpr);
                }

                expressions.Add(Expression.Convert(instanceVar, typeof(object)));

                var body = Expression.Block(new[] { instanceVar }, expressions);
                var lambda = Expression.Lambda<Func<IDataReader, ValueConverterCollection, object>>(body, readerParam, convertersParam);
                return lambda.Compile();
            }
            catch
            {
                return BuildFallbackMaterializer(targetType, reader);
            }
        }

        private static Func<IDataReader, ValueConverterCollection, object> BuildFallbackMaterializer(Type targetType, IDataReader reader)
        {
            EntityMapping mapping = null;
            try { mapping = MappingCache.GetMapping(targetType); } catch { }

            var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToList();

            var bindings = new List<(int Ordinal, PropertyInfo Prop)>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                PropertyInfo prop = null;

                if (mapping != null)
                {
                    var colMapping = mapping.Columns.FirstOrDefault(c =>
                        string.Equals(c.ColumnName, colName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Property.Name, colName, StringComparison.OrdinalIgnoreCase));
                    if (colMapping != null) prop = colMapping.Property;
                }

                if (prop == null)
                {
                    prop = properties.FirstOrDefault(p =>
                        string.Equals(p.Name, colName, StringComparison.OrdinalIgnoreCase));
                }

                if (prop != null && prop.CanWrite)
                {
                    bindings.Add((i, prop));
                }
            }

            var bindingsArray = bindings.ToArray();

            return (r, conv) =>
            {
                var entity = Activator.CreateInstance(targetType);
                for (int i = 0; i < bindingsArray.Length; i++)
                {
                    var (ordinal, prop) = bindingsArray[i];
                    if (!r.IsDBNull(ordinal))
                    {
                        var raw = r.GetValue(ordinal);
                        object val;
                        if (conv != null && conv.HasConverter(prop.PropertyType))
                        {
                            val = conv.ConvertFromDb(raw, prop.PropertyType);
                        }
                        else
                        {
                            val = FastConvert.ChangeType(raw, prop.PropertyType);
                        }
                        prop.SetValue(entity, val, null);
                    }
                }
                return entity;
            };
        }

        /// <summary>
        /// Clears all compiled materialization delegates.
        /// </summary>
        public static void ClearCache()
        {
            DelegateCache.Clear();
        }
    }
}
