using System.Data.SQLite;
using NUnit.Framework;
using OfflineDaoc.Passport;

namespace OfflineDaoc.Launcher.Tests;

public sealed class JoinFriendTravelTests
{
    private string _runtime = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _runtime = Path.Combine(Path.GetTempPath(), "odc-travel-" + Guid.NewGuid().ToString("N"), "runtime");
        Directory.CreateDirectory(Path.Combine(_runtime, "data"));
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        Directory.Delete(Path.GetDirectoryName(_runtime)!, true);
    }

    private void WriteSave()
    {
        string database = Path.Combine(_runtime, "data", "opendaoc.sqlite3.db");
        SQLiteConnection.CreateFile(database);
        using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = database, Pooling = false }.ConnectionString);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT PRIMARY KEY, AccountName TEXT, Name TEXT, Realm INT, Level INT);
            CREATE TABLE DOLCharactersXCustomParam (DOLCharactersObjectId TEXT, KeyName TEXT, Value TEXT, CustomParamID INTEGER PRIMARY KEY AUTOINCREMENT);
            INSERT INTO DOLCharacters VALUES ('a','offline','Athome',1,50),('b','offline','Awayone',2,12),('c','aibot','Notmine',3,1);
            INSERT INTO DOLCharactersXCustomParam (DOLCharactersObjectId,KeyName,Value) VALUES ('b','passport.away','trip'),('b','passport.host','100.90.1.2');
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public void HomeCharactersListsThisPlayersCharactersOnly()
    {
        WriteSave();
        File.WriteAllText(Path.Combine(_runtime, "account.txt"), "Account: offline\r\nPassword: x\r\n");

        IReadOnlyList<LocalCharacter> characters = JoinFriend.HomeCharacters(_runtime);

        Assert.That(characters.Select(JoinFriend.DescribeCharacter), Is.EqualTo(new[]
            { "Athome, level 50, Albion", "Awayone, level 12, Midgard (away at 100.90.1.2)" }));
    }

    [Test]
    public void AJoinOnlyInstallHasNoCharacters()
    {
        Assert.That(JoinFriend.HomeCharacters(_runtime), Is.Empty);
        Assert.That(File.Exists(Path.Combine(_runtime, "account.txt")), Is.False, "Looking must not make an account file.");
    }

    [Test]
    public void TravelMessagesSayWhatToDo()
    {
        Assert.That(JoinFriend.TravelMessage(new PassportException("server_running", "x")), Does.StartWith("Stop your own server"));
        Assert.That(JoinFriend.TravelMessage(new PassportException("name_taken", "Taken.")), Does.Contain("visiting name"));
        Assert.That(JoinFriend.TravelMessage(new PassportException("no_slot", "No slot.")), Is.EqualTo("No slot."));
    }
}
