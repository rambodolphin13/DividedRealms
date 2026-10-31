using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace OfflineDaoc.TestClient
{
    public sealed record ZoneInfo(int Id, int Region, string Name, int X, int Y, int Width, int Height)
    {
        public bool Contains(double x, double y)
        {
            double bx = x / 8192.0, by = y / 8192.0;
            return bx >= X && bx < X + Width && by >= Y && by < Y + Height;
        }
    }

    // Where the test install is, and everything the client takes from it.
    public sealed class TestConfig
    {
        // The account is a normal player: GamePlayer.IsAttackable is false above player level, so a real GM can't be
        // hit and combat tests would prove nothing. These GM commands are granted one by one (/plvl singleaccount).
        // Admin commands, ban, kick and the like stay out.
        static readonly string[] GrantedCommands =
        {
            "GMinfo", "blink", "cast", "clearinventory", "crafting", "debug", "fly", "gmstealth", "harm", "heal",
            "item", "jump", "mob", "morph", "npc", "object", "offlinejump", "player", "speed", "spammob", "stop",
            "taxitest", "teleport", "walk", "weather", "zone",
        };

        public string Runtime { get; }
        public string Host { get; } = "127.0.0.1";
        public int Port { get; }
        public string AccountPrefix { get; }
        public string Database => Path.Combine(Runtime, "data", "opendaoc.sqlite3.db");
        public string NavmeshFolder => Path.Combine(Runtime, "server", "navmesh");
        public string DetourDll => Path.Combine(Runtime, "server", "lib", "Detour.dll");
        public IReadOnlyList<ZoneInfo> Zones { get; }

        // Outside the install, so no package can ever carry them.
        static string AccountsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineDAoC", "test-client-accounts.json");

        public TestConfig(string runtime, string accountPrefix)
        {
            Runtime = Path.GetFullPath(runtime);
            AccountPrefix = accountPrefix;
            if (!File.Exists(Database))
                throw new FileNotFoundException("No world database in the runtime folder", Database);
            var server = XDocument.Load(Path.Combine(Runtime, "server", "config", "serverconfig.xml")).Descendants("Port").First();
            Port = int.Parse(server.Value);
            Zones = LoadZones();
        }

        List<ZoneInfo> LoadZones()
        {
            var zones = new List<ZoneInfo>();
            using var db = Open(readOnly: true);
            using var query = new SQLiteCommand("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones", db);
            using var row = query.ExecuteReader();
            while (row.Read())
                zones.Add(new ZoneInfo(row.GetInt32(0), row.GetInt32(1), row.GetString(2), row.GetInt32(3), row.GetInt32(4), row.GetInt32(5), row.GetInt32(6)));
            return zones;
        }

        public ZoneInfo ZoneAt(int region, double x, double y)
        {
            return Zones.FirstOrDefault(z => z.Region == region && z.Contains(x, y));
        }

        SQLiteConnection Open(bool readOnly)
        {
            var db = new SQLiteConnection($"Data Source={Database};Read Only={readOnly};BusyTimeout=10000;Pooling=False");
            db.Open();
            return db;
        }

        // A password nobody knows: a committed one would be a GM login on a world hosted for friends.
        public string PasswordFor(string account)
        {
            var accounts = File.Exists(AccountsFile)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AccountsFile))
                : new Dictionary<string, string>();
            if (!accounts.TryGetValue(account, out string password))
            {
                accounts[account] = password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                Directory.CreateDirectory(Path.GetDirectoryName(AccountsFile));
                File.WriteAllText(AccountsFile, JsonSerializer.Serialize(accounts));
            }
            return password;
        }

        // DbAccount.IsTester keeps the account out of the server firsts. False: the account doesn't exist yet.
        public bool MarkTester(string account)
        {
            using var db = Open(readOnly: false);
            using var update = new SQLiteCommand("update Account set IsTester = 1 where Name = @n", db);
            update.Parameters.AddWithValue("@n", account);
            return update.ExecuteNonQuery() > 0;
        }

        public void GrantCommands(string account)
        {
            using var db = Open(readOnly: false);
            foreach (string command in GrantedCommands)
            {
                using var exists = new SQLiteCommand("select count(*) from SinglePermission where PlayerID = @p and Command = @c", db);
                exists.Parameters.AddWithValue("@p", account);
                exists.Parameters.AddWithValue("@c", command);
                if ((long)exists.ExecuteScalar() > 0)
                    continue;
                using var insert = new SQLiteCommand(
                    "insert into SinglePermission (PlayerID, Command, LastTimeRowUpdated, SinglePermission_ID) values (@p, @c, @t, @i)", db);
                insert.Parameters.AddWithValue("@p", account);
                insert.Parameters.AddWithValue("@c", command);
                insert.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                insert.Parameters.AddWithValue("@i", Guid.NewGuid().ToString());
                insert.ExecuteNonQuery();
            }
        }
    }
}
