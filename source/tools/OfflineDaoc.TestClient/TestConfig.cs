using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace OfflineDaoc.TestClient
{
    public sealed record ZoneInfo(int Id, int Region, string Name, int X, int Y, int Width, int Height, int WaterLevel)
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
        public string SharedLog { get; }
        public string Database => Path.Combine(Runtime, "data", "opendaoc.sqlite3.db");
        public string NavmeshFolder => Path.Combine(Runtime, "server", "navmesh");
        public string DetourDll => Path.Combine(Runtime, "server", "lib", "Detour.dll");
        public IReadOnlyList<ZoneInfo> Zones { get; }

        // Outside the install, so no package can ever carry them.
        static string StateFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineDAoC");
        static string AccountsFile => Path.Combine(StateFolder, "test-client-accounts.json");

        public TestConfig(string runtime, string accountPrefix, string sharedLog)
        {
            SharedLog = sharedLog ?? Path.Combine(StateFolder, "test-findings.jsonl");
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
            using var query = new SQLiteCommand("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height, WaterLevel from Zones", db);
            using var row = query.ExecuteReader();
            while (row.Read())
                zones.Add(new ZoneInfo(row.GetInt32(0), row.GetInt32(1), row.GetString(2), row.GetInt32(3), row.GetInt32(4), row.GetInt32(5), row.GetInt32(6), row.GetInt32(7)));
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
            using var turn = new Mutex(false, @"Local\OfflineDAoC.TestClient.Accounts");
            turn.WaitOne();
            try
            {
                var accounts = File.Exists(AccountsFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AccountsFile))
                    : new Dictionary<string, string>();
                if (!accounts.TryGetValue(account, out string password))
                {
                    accounts[account] = password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                    Directory.CreateDirectory(StateFolder);
                    File.WriteAllText(AccountsFile, JsonSerializer.Serialize(accounts));
                }
                return password;
            }
            finally
            {
                turn.ReleaseMutex();
            }
        }

        // Held for the session's life, and freed by Windows if the process dies: two agents on one account would keep
        // refusing and dropping each other.
        public FileStream ClaimAccount(string account)
        {
            string folder = Path.Combine(StateFolder, "sessions");
            Directory.CreateDirectory(folder);
            try
            {
                return new FileStream(Path.Combine(folder, account + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new InvalidOperationException($"Account {account} is in use by another agent: choose another session name.");
            }
        }

        // Read only by its connection, so a test can't change the world through here.
        public List<Dictionary<string, object>> Query(string sql, int limit)
        {
            var rows = new List<Dictionary<string, object>>();
            using var db = Open(readOnly: true);
            using var query = new SQLiteCommand(sql, db);
            using var row = query.ExecuteReader();
            while (rows.Count < limit && row.Read())
            {
                var values = new Dictionary<string, object>();
                for (int i = 0; i < row.FieldCount; i++)
                    values[row.GetName(i)] = row.IsDBNull(i) ? null : row.GetValue(i);
                rows.Add(values);
            }
            return rows;
        }

        // One line a call, so several programs can add to the log together.
        public void AppendShared(object entry)
        {
            byte[] line = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SharedLog)));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var log = new FileStream(SharedLog, FileMode.Append, FileAccess.Write, FileShare.Read);
                    log.Write(line);
                    return;
                }
                catch (IOException) when (attempt < 50)
                {
                    Thread.Sleep(20);
                }
            }
        }

        public List<string> ReadShared()
        {
            if (!File.Exists(SharedLog))
                return new List<string>();
            using var log = new FileStream(SharedLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(log);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        // Made here rather than by a first login: auto creation stops at 20 accounts from one address, and the server reads
        // the row at login, so a flag set afterwards would miss that session. LoginRequestHandler accepts a plain password
        // and hashes it on first use. DbAccount.IsTester keeps the account out of the server firsts.
        public void EnsureAccount(string account)
        {
            string password = PasswordFor(account);
            using var db = Open(readOnly: false);
            using var update = new SQLiteCommand("update Account set IsTester = 1 where Name = @n", db);
            update.Parameters.AddWithValue("@n", account);
            if (update.ExecuteNonQuery() > 0)
                return;
            using var insert = new SQLiteCommand(
                "insert into Account (Name, Password, CreationDate, PrivLevel, Language, IsTester, Account_ID, LastTimeRowUpdated) "
                + "values (@n, @p, @t, 1, 'EN', 1, @i, @t)", db);
            insert.Parameters.AddWithValue("@n", account);
            insert.Parameters.AddWithValue("@p", password);
            insert.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            insert.Parameters.AddWithValue("@i", Guid.NewGuid().ToString());
            insert.ExecuteNonQuery();
        }

        // The modern classes' StartupLocation rows say MinVersion 1130 and this client announces 1.127, so such a character
        // is created nowhere and can't load. It goes where a 1.130 client would have put it. True: it was moved.
        public bool PlaceIfNowhere(string name)
        {
            using var db = Open(readOnly: false);
            using var move = new SQLiteCommand(
                "update DOLCharacters set (Region, Xpos, Ypos, Zpos, Direction, BindRegion, BindXpos, BindYpos, BindZpos, BindHeading) = "
                + "(select s.Region, s.XPos, s.YPos, s.ZPos, s.Heading, s.Region, s.XPos, s.YPos, s.ZPos, s.Heading from StartupLocation s "
                + "where (s.ClassID = 0 or s.ClassID = DOLCharacters.Class) and (s.RaceID = 0 or s.RaceID = DOLCharacters.Race) "
                + "and (s.RealmID = 0 or s.RealmID = DOLCharacters.Realm) order by s.MinVersion desc, s.ClassID desc limit 1) "
                + "where Name = @n and Region = 0 and Xpos = 0", db);
            move.Parameters.AddWithValue("@n", name);
            return move.ExecuteNonQuery() > 0;
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
