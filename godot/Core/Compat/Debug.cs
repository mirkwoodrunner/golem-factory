using System;

namespace GolemFactory.Compat
{
    // Unity's Debug.Log* for the few rules that warn about an authoring mistake.
    // Core cannot reach an engine console, so it raises an event; the Godot layer
    // forwards it to GD.Print/GD.PushWarning/GD.PushError, and with nobody
    // listening (a plain `dotnet test` run) it falls back to stderr.
    public static class Debug
    {
        public static event Action<string> Logged;
        public static event Action<string> Warned;
        public static event Action<string> Errored;

        public static void Log(object message) => Raise(Logged, message, "");
        public static void LogWarning(object message) => Raise(Warned, message, "warning: ");
        public static void LogError(object message) => Raise(Errored, message, "error: ");

        private static void Raise(Action<string> handler, object message, string prefix)
        {
            string text = message?.ToString() ?? "null";
            if (handler != null)
            {
                handler(text);
            }
            else
            {
                Console.Error.WriteLine(prefix + text);
            }
        }
    }
}
