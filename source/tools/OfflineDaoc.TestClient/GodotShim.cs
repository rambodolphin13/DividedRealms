// The few Godot types DaocClient.cs touches, so the new client's protocol code compiles without the engine.
// EmitSignal goes to one event instead of Godot's signals; the collections are plain boxed values.

namespace Godot
{
    [AttributeUsage(AttributeTargets.Delegate)]
    public sealed class SignalAttribute : Attribute
    {
    }

    public class Node
    {
        public event Action<string, object[]> Signalled;

        public virtual void _Process(double delta)
        {
        }

        public void EmitSignal(string signal, params object[] args)
        {
            Signalled?.Invoke(signal, args);
        }
    }

    public static class GD
    {
        public static void PrintErr(string text)
        {
            Console.Error.WriteLine(text);
        }
    }

    public static class Time
    {
        public static ulong GetTicksMsec()
        {
            return (ulong)Environment.TickCount64;
        }
    }
}

namespace Godot.Collections
{
    public class Dictionary : Dictionary<string, object>
    {
    }

    public class Array : List<object>
    {
    }
}

namespace MMOEngine
{
    // Godot generates this class from the [Signal] delegates.
    public partial class DaocClient
    {
        public static class SignalName
        {
            public const string Connected = nameof(Connected);
            public const string Disconnected = nameof(Disconnected);
            public const string LoggedIn = nameof(LoggedIn);
            public const string LoginFailed = nameof(LoginFailed);
            public const string Realm = nameof(Realm);
            public const string Characters = nameof(Characters);
            public const string Session = nameof(Session);
            public const string Packet = nameof(Packet);
            public const string World = nameof(World);
        }
    }
}
