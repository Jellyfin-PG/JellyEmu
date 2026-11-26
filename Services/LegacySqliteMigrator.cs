using System;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Reads legacy SQLite .db files at runtime via Jellyfin's already-loaded Microsoft.Data.Sqlite
    /// assembly (via reflection) so JellyEmu maintains zero compile-time or package dependence on SQLite.
    /// </summary>
    internal static class LegacySqliteMigrator
    {
        public static bool TryReadTable(
            string dbPath,
            string query,
            Action<IDataReader> onRow,
            ILogger? logger = null)
        {
            var connType = ResolveSqliteConnectionType();
            if (connType == null)
            {
                logger?.LogWarning("[JellyEmu] Microsoft.Data.Sqlite is not loaded; skipping legacy SQLite migration for {DbPath}.", dbPath);
                return false;
            }

            IDbConnection? conn = null;
            try
            {
                var connStr = $"Data Source={dbPath};Mode=ReadOnly;Pooling=False";
                conn = (IDbConnection?)Activator.CreateInstance(connType, connStr);
                if (conn == null) return false;

                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    onRow(reader);
                }

                return true;
            }
            finally
            {
                if (conn != null)
                {
                    try { conn.Close(); } catch { }
                    try { conn.Dispose(); } catch { }
                    try
                    {
                        var clearPool = connType.GetMethod("ClearPool", BindingFlags.Public | BindingFlags.Static);
                        clearPool?.Invoke(null, new object[] { conn });
                    }
                    catch { }
                }
            }
        }

        private static Type? ResolveSqliteConnectionType()
        {
            var direct = Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite");
            if (direct != null) return direct;

            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase))
                ?.GetType("Microsoft.Data.Sqlite.SqliteConnection");
        }
    }
}
