using System.Security.Cryptography;

namespace OfflineDaoc.Launcher;

/// <summary>An account this game folder can log in with.</summary>
public sealed record SavedAccount(string Account, string Password);

// Portable-only bootstrap: each extracted installation generates its own local
// credentials. The server creates the account on first successful client login.
//
// account.txt is the account in use: the client, overlay, GM switch and Join Friend all read it.
// accounts.txt beside it keeps every account this folder has made (name and password), so the
// player can make more accounts and switch between them without ever losing a password.
public static class PortableCredentials
{
    public const string FileName = "account.txt";
    public const string BookFileName = "accounts.txt";
    // The 1.130 login packet has 20-character name and password fields; the server takes letters and digits only.
    public const int MaxNameLength = 20;

    public static (string Account, string Password) ReadOrCreate(string runtime)
    {
        string path = Path.Combine(runtime, FileName);
        if (!File.Exists(path))
        {
            string content = Format(new SavedAccount("offline", NewPassword()));
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(file);
                writer.Write(content);
            }
            catch (IOException) when (File.Exists(path)) { /* Another launcher created it first. Never replace it. */ }
        }
        var values = File.ReadLines(path).Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("Account", out string? account) || string.IsNullOrWhiteSpace(account) ||
            !values.TryGetValue("Password", out string? password) || string.IsNullOrEmpty(password))
            throw new InvalidDataException("Local account.txt is invalid. Restore it from your progress backup; do not delete existing account credentials.");
        return (account, password);
    }

    /// <summary>Why this name can't be an account, or null when it can.</summary>
    public static string? NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Type a name for the account.";
        if (name.Length < 3 || name.Length > MaxNameLength)
            return $"Account names are 3 to {MaxNameLength} letters or digits.";
        if (!name.All(char.IsAsciiLetterOrDigit))
            return "Account names use only letters and digits (no spaces or symbols).";
        return null;
    }

    /// <summary>Every saved account, the one in use first. The account in use is always kept in the book.</summary>
    public static IReadOnlyList<SavedAccount> List(string runtime)
    {
        var (current, password) = ReadOrCreate(runtime);
        List<SavedAccount> book = ReadBook(runtime);
        if (!book.Any(a => Same(a.Account, current)))
        {
            book.Insert(0, new SavedAccount(current, password));
            WriteBook(runtime, book);
        }
        return book.OrderByDescending(a => Same(a.Account, current)).ThenBy(a => a.Account, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Saves a new account with its own random password (the server creates it on its first login).</summary>
    public static SavedAccount Create(string runtime, string name)
    {
        name = name.Trim();
        if (NameProblem(name) is { } problem)
            throw new ArgumentException(problem);
        var book = List(runtime).ToList();
        if (book.Any(a => Same(a.Account, name)))
            throw new ArgumentException($"There is already an account named {name}.");
        var account = new SavedAccount(name, NewPassword());
        book.Add(account);
        WriteBook(runtime, book);
        return account;
    }

    /// <summary>Makes a saved account the one the game logs in with.</summary>
    public static SavedAccount Use(string runtime, string name)
    {
        SavedAccount account = List(runtime).FirstOrDefault(a => Same(a.Account, name))
            ?? throw new ArgumentException($"No saved account named {name}.");
        WriteAtomically(Path.Combine(runtime, FileName), Format(account));
        return account;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // The legacy DAoC login packet has a 20-character password field.
    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    private static string Format(SavedAccount account) => $"Account: {account.Account}\r\nPassword: {account.Password}\r\n";

    private static List<SavedAccount> ReadBook(string runtime)
    {
        string path = Path.Combine(runtime, BookFileName);
        var accounts = new List<SavedAccount>();
        if (!File.Exists(path))
            return accounts;
        string? name = null;
        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(':', 2);
            if (parts.Length != 2)
                continue;
            string key = parts[0].Trim(), value = parts[1].Trim();
            if (key.Equals("Account", StringComparison.OrdinalIgnoreCase))
                name = value;
            else if (key.Equals("Password", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name) && value.Length > 0)
            {
                if (!accounts.Any(a => Same(a.Account, name)))
                    accounts.Add(new SavedAccount(name, value));
                name = null;
            }
        }
        return accounts;
    }

    private static void WriteBook(string runtime, IEnumerable<SavedAccount> accounts) =>
        WriteAtomically(Path.Combine(runtime, BookFileName),
            "# Accounts this game folder can log in with. Keep this file: a lost password can't be recovered.\r\n\r\n" +
            string.Join("\r\n", accounts.Select(Format)));

    // Written beside the file and swapped in, so a crash never leaves half a file (and a lost password).
    private static void WriteAtomically(string path, string content)
    {
        string temp = path + ".new";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
