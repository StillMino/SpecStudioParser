using Microsoft.Data.SqlClient;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpecStudioParser.CadLib
{
    public interface ICadLibDbProvider
    {
        Task<IReadOnlyList<CadLibDatabaseInfo>> GetDatabasesAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default);
        Task ValidateCadLibSchemaAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CadLibParameterInfo>> GetParametersAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default);
    }

    public sealed class CadLibConnectionService
    {
        public Task<IReadOnlyList<CadLibDatabaseInfo>> GetDatabasesAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            return CreateProvider(settings.ProviderKind).GetDatabasesAsync(settings, cancellationToken);
        }

        public async Task<IReadOnlyList<CadLibParameterInfo>> ConnectAndLoadParametersAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            var provider = CreateProvider(settings.ProviderKind);
            await provider.ValidateCadLibSchemaAsync(settings, cancellationToken).ConfigureAwait(false);
            return await provider.GetParametersAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        private static ICadLibDbProvider CreateProvider(CadLibDatabaseProviderKind providerKind)
        {
            return providerKind == CadLibDatabaseProviderKind.MsSqlServer
                ? new MsSqlCadLibDbProvider()
                : new PostgresCadLibDbProvider();
        }
    }

    public sealed class PostgresCadLibDbProvider : ICadLibDbProvider
    {
        public async Task<IReadOnlyList<CadLibDatabaseInfo>> GetDatabasesAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            var result = new List<CadLibDatabaseInfo>();
            await using var connection = new NpgsqlConnection(BuildConnectionString(settings, "postgres"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(new CadLibDatabaseInfo { Name = reader.GetString(0) });
            }

            return result;
        }

        public async Task ValidateCadLibSchemaAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            await using var connection = new NpgsqlConnection(BuildConnectionString(settings, settings.DatabaseName));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var schema = await LoadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!schema.ContainsKey("ParamDefs"))
            {
                throw new InvalidOperationException("Выбранная база не похожа на CADLib Модель и Архив: отсутствует таблица ParamDefs.");
            }
        }

        public async Task<IReadOnlyList<CadLibParameterInfo>> GetParametersAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            var result = new List<CadLibParameterInfo>();
            await using var connection = new NpgsqlConnection(BuildConnectionString(settings, settings.DatabaseName));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var schema = await LoadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            var sql = CadLibParameterSqlBuilder.Build(schema, quote: s => $"\"{s.Replace("\"", "\"\"")}\"", coalesce: "COALESCE");

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(CadLibParameterSqlBuilder.ReadParameter(reader));
            }

            return result;
        }

        private static string BuildConnectionString(CadLibConnectionSettings settings, string databaseName)
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = settings.Host,
                Port = settings.Port > 0 ? settings.Port : 5432,
                Username = settings.EffectiveLogin,
                Password = settings.EffectivePassword,
                Database = string.IsNullOrWhiteSpace(databaseName) ? "postgres" : databaseName,
                Timeout = 10,
                CommandTimeout = 30,
                Pooling = false
            }.ConnectionString;
        }

        private static async Task<Dictionary<string, HashSet<string>>> LoadSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' ORDER BY table_name, ordinal_position;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                CadLibParameterSqlBuilder.AddSchemaColumn(schema, reader.GetString(0), reader.GetString(1));
            }

            return schema;
        }
    }

    public sealed class MsSqlCadLibDbProvider : ICadLibDbProvider
    {
        public async Task<IReadOnlyList<CadLibDatabaseInfo>> GetDatabasesAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            var result = new List<CadLibDatabaseInfo>();
            await using var connection = new SqlConnection(BuildConnectionString(settings, "master"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(new CadLibDatabaseInfo { Name = reader.GetString(0) });
            }

            return result;
        }

        public async Task ValidateCadLibSchemaAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(BuildConnectionString(settings, settings.DatabaseName));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var schema = await LoadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!schema.ContainsKey("ParamDefs"))
            {
                throw new InvalidOperationException("Выбранная база не похожа на CADLib Модель и Архив: отсутствует таблица ParamDefs.");
            }
        }

        public async Task<IReadOnlyList<CadLibParameterInfo>> GetParametersAsync(CadLibConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            var result = new List<CadLibParameterInfo>();
            await using var connection = new SqlConnection(BuildConnectionString(settings, settings.DatabaseName));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var schema = await LoadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            var sql = CadLibParameterSqlBuilder.Build(schema, quote: s => $"[{s.Replace("]", "]]")}]", coalesce: "ISNULL");

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(CadLibParameterSqlBuilder.ReadParameter(reader));
            }

            return result;
        }

        private static string BuildConnectionString(CadLibConnectionSettings settings, string databaseName)
        {
            return new SqlConnectionStringBuilder
            {
                DataSource = $"{settings.Host},{(settings.Port > 0 ? settings.Port : 1433)}",
                UserID = settings.EffectiveLogin,
                Password = settings.EffectivePassword,
                InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName,
                ConnectTimeout = 10,
                TrustServerCertificate = true,
                Encrypt = false,
                Pooling = false
            }.ConnectionString;
        }

        private static async Task<Dictionary<string, HashSet<string>>> LoadSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME, ORDINAL_POSITION;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                CadLibParameterSqlBuilder.AddSchemaColumn(schema, reader.GetString(0), reader.GetString(1));
            }

            return schema;
        }
    }

    internal static class CadLibParameterSqlBuilder
    {
        public static void AddSchemaColumn(Dictionary<string, HashSet<string>> schema, string table, string column)
        {
            if (!schema.TryGetValue(table, out var columns))
            {
                columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                schema[table] = columns;
            }

            columns.Add(column);
        }

        public static CadLibParameterInfo ReadParameter(DbDataReader reader)
        {
            return new CadLibParameterInfo
            {
                IdParamDef = SafeInt(reader, 0),
                SystemName = SafeString(reader, 1),
                DisplayName = SafeString(reader, 2),
                TypeId = SafeInt(reader, 3),
                CategoryName = SafeString(reader, 4),
                Comment = SafeString(reader, 5)
            };
        }

        public static string Build(Dictionary<string, HashSet<string>> schema, Func<string, string> quote, string coalesce)
        {
            var paramDefs = FindTable(schema, "ParamDefs", required: true);
            var paramCategory = FindTable(schema, "ParamCategory", required: false);
            var parametersStr = FindTable(schema, "Parameters_STR", required: false);

            var pdId = FindColumn(schema, paramDefs, "idParamDef", required: true);
            var pdName = FindColumn(schema, paramDefs, "Name", required: true);
            var pdCaption = FindColumn(schema, paramDefs, "Caption", required: true);
            var pdType = FindColumn(schema, paramDefs, "idType", required: false);

            var joins = new List<string>();
            var categoryExpr = "''";
            var commentExpr = "''";
            var commentJoin = string.Empty;

            if (schema.ContainsKey(paramCategory))
            {
                var pcId = FindColumn(schema, paramCategory, "idParamCategory", required: false);
                var pcName = FindColumn(schema, paramCategory, "Name", required: false);

                if (schema[paramDefs].Contains("idParamCategory"))
                {
                    var pdCategory = FindColumn(schema, paramDefs, "idParamCategory", required: false);
                    joins.Add($"LEFT JOIN {quote(paramCategory)} pc ON pc.{quote(pcId)} = pd.{quote(pdCategory)}");
                    categoryExpr = $"{coalesce}(pc.{quote(pcName)}, '')";
                }
                else
                {
                    var linkTable = schema.Keys.FirstOrDefault(t =>
                        schema[t].Contains("idParamDef") && schema[t].Contains("idParamCategory") &&
                        !string.Equals(t, paramDefs, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(t, paramCategory, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(linkTable))
                    {
                        var linkParam = FindColumn(schema, linkTable!, "idParamDef", required: false);
                        var linkCategory = FindColumn(schema, linkTable!, "idParamCategory", required: false);
                        joins.Add($"LEFT JOIN {quote(linkTable!)} pdc ON pdc.{quote(linkParam)} = pd.{quote(pdId)}");
                        joins.Add($"LEFT JOIN {quote(paramCategory)} pc ON pc.{quote(pcId)} = pdc.{quote(linkCategory)}");
                        categoryExpr = $"{coalesce}(pc.{quote(pcName)}, '')";
                    }
                }
            }

            if (schema.ContainsKey(parametersStr) && schema[parametersStr].Contains("idParamDef") && schema[parametersStr].Contains("Comment"))
            {
                var psParam = FindColumn(schema, parametersStr, "idParamDef", required: false);
                var psComment = FindColumn(schema, parametersStr, "Comment", required: false);
                commentJoin = $@"
LEFT JOIN (
    SELECT {quote(psParam)} AS id_param_def, MAX(NULLIF({quote(psComment)}, '')) AS comment_value
    FROM {quote(parametersStr)}
    GROUP BY {quote(psParam)}
) ps ON ps.id_param_def = pd.{quote(pdId)}";
                commentExpr = $"{coalesce}(ps.comment_value, '')";
            }

            return $@"
SELECT
    pd.{quote(pdId)} AS id_param_def,
    pd.{quote(pdName)} AS system_name,
    {coalesce}(pd.{quote(pdCaption)}, '') AS display_name,
    {coalesce}(pd.{quote(pdType)}, 0) AS type_id,
    {categoryExpr} AS category_name,
    {commentExpr} AS parameter_comment
FROM {quote(paramDefs)} pd
{string.Join(Environment.NewLine, joins)}
{commentJoin}
ORDER BY category_name, display_name, system_name;";
        }

        private static string FindTable(Dictionary<string, HashSet<string>> schema, string logicalName, bool required)
        {
            var table = schema.Keys.FirstOrDefault(t => string.Equals(t, logicalName, StringComparison.OrdinalIgnoreCase));
            if (table == null && required)
            {
                throw new InvalidOperationException($"В CADLib БД отсутствует таблица {logicalName}.");
            }

            return table ?? logicalName;
        }

        private static string FindColumn(Dictionary<string, HashSet<string>> schema, string table, string logicalName, bool required)
        {
            if (!schema.TryGetValue(table, out var columns))
            {
                if (required) throw new InvalidOperationException($"В CADLib БД отсутствует таблица {table}.");
                return logicalName;
            }

            var column = columns.FirstOrDefault(c => string.Equals(c, logicalName, StringComparison.OrdinalIgnoreCase));
            if (column == null && required)
            {
                throw new InvalidOperationException($"В таблице {table} отсутствует поле {logicalName}.");
            }

            return column ?? logicalName;
        }

        private static int SafeInt(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0;
            return Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static string SafeString(DbDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
        }
    }
}
