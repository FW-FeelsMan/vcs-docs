using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace VCS_DOCs.Support.Integration
{
    /// <summary>
    /// Адаптер к БД V-Docs (SQLite):
    /// - presence: читает AspNetUsers(Id, StatusOnline)
    /// - kick:     UPDATE AspNetUsers SET StatusOnline=0 WHERE Id=@id
    /// </summary>
    public sealed class SqliteVDocsAdapter : IExternalProjectAdapter
    {
        private readonly string _connectionString;

        public SqliteVDocsAdapter(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public string AppCode => AppCodes.VDocs;

        public async Task<IDictionary<string, PresenceInfo>> GetPresenceManyAsync(IEnumerable<string> userIds, CancellationToken ct = default)
        {
            var ids = (userIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            var map = ids.ToDictionary(id => id, _ => new PresenceInfo(false, null), StringComparer.Ordinal);
            if (ids.Length == 0) return map;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync(ct);

            // SQLite IN (...) с параметрами
            var parms = new List<SqliteParameter>();
            var inClause = string.Join(",", ids.Select((id, i) =>
            {
                var p = new SqliteParameter($"@p{i}", id);
                parms.Add(p);
                return p.ParameterName;
            }));

            // ВАЖНО: правильное имя столбца — StatusOnline
            var sql = $"SELECT Id, StatusOnline FROM AspNetUsers WHERE Id IN ({inClause})";

            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parms.ToArray());

            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var id = rdr.GetString(0);

                // StatusOnline может быть INTEGER(0/1) или BOOLEAN
                var isOnline = false;
                var val = rdr.GetValue(1);
                if (val is long l) isOnline = l != 0;
                else if (val is int i) isOnline = i != 0;
                else if (val is bool b) isOnline = b;

                map[id] = new PresenceInfo(isOnline, null);
            }

            return map;
        }

        public async Task KickAsync(string userId, CancellationToken ct = default)
        {
            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync(ct);

            using var cmd = con.CreateCommand();
            // Тоже StatusOnline
            cmd.CommandText = "UPDATE AspNetUsers SET StatusOnline = 0 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
