using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GK2Trainer
{
    /// <summary>
    /// File based bridge between the game and the desktop app.
    ///
    ///   command.json   app  -> game   {"token":"..","seq":1,"ops":[..]}
    ///   state.json     game -> app    {"token":"..","seq":1,"targets":[..]}
    ///   log.txt        game -> app    diagnostics
    ///
    /// All paths live under the game's persistent data folder
    /// (%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Trainer).
    /// The protocol itself is documented in the game-trainer skill
    /// (references/bridge-protocol.md); the de-duplication rule below is the
    /// important part: the last executed (token, seq) is persisted in state.json
    /// so that reloading a save can never replay the last command.
    /// </summary>
    internal static class Bridge
    {
        public const string Version = "0.1.0";

        private static string _dir;
        private static string _commandPath;
        private static string _statePath;
        private static string _logPath;

        private static readonly List<string> LogLines = new List<string>();
        private static bool _logDirty;

        public static string Directory_ => _dir;

        public static void Initialise(string baseDirectory)
        {
            _dir = Path.Combine(baseDirectory, "Trainer");
            _commandPath = Path.Combine(_dir, "command.json");
            _statePath = Path.Combine(_dir, "state.json");
            _logPath = Path.Combine(_dir, "log.txt");
            System.IO.Directory.CreateDirectory(_dir);
        }

        // ---------------------------------------------------------------- log

        public static void Log(string message)
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (LogLines.Count > 300) LogLines.RemoveRange(0, LogLines.Count - 300);
            _logDirty = true;
        }

        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

        /// <summary>
        /// Logs a message only the first time a given key is seen. Used for
        /// conditions that repeat every frame (eg. "not in game yet"), which would
        /// otherwise flood the log.
        /// </summary>
        public static void LogOnce(string key, string message)
        {
            if (LoggedOnce.Contains(key)) return;
            LoggedOnce.Add(key);
            Log(message);
        }

        public static void FlushLog()
        {
            if (!_logDirty || _logPath == null) return;
            _logDirty = false;

            try
            {
                var header = $"--- GK2 Trainer {Version} | unity {Snapshot.UnityVersion} | {LogLines.Count} line(s) ---";
                File.WriteAllText(_logPath, header + Environment.NewLine + string.Join(Environment.NewLine, LogLines) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Logging must never take the game down.
            }
        }

        // -------------------------------------------------------------- state

        /// <summary>Writes the state document atomically (temp file + replace).</summary>
        public static void WriteState(object state)
        {
            try
            {
                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                var temp = _statePath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(_statePath)) File.Replace(temp, _statePath, null);
                else File.Move(temp, _statePath);
            }
            catch (Exception ex)
            {
                Log("writeState failed: " + ex.Message);
            }
        }

        /// <summary>Reads the persisted acknowledgement (token + seq) from state.json.</summary>
        public static void LoadAck(out string token, out long seq)
        {
            token = "";
            seq = 0;
            try
            {
                if (!File.Exists(_statePath)) return;
                var json = JObject.Parse(File.ReadAllText(_statePath));
                token = (string)json["token"] ?? "";
                seq = (long?)json["seq"] ?? 0;
            }
            catch (Exception ex)
            {
                Log("loadAck failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the pending command. A half written or unparsable file is left
        /// alone and retried on the next tick.
        /// </summary>
        public static JObject ReadCommand()
        {
            try
            {
                if (!File.Exists(_commandPath)) return null;
                var text = File.ReadAllText(_commandPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JObject.Parse(text);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log("readCommand failed: " + ex.Message);
                return null;
            }
        }

        public static void DeleteCommand()
        {
            try
            {
                if (File.Exists(_commandPath)) File.Delete(_commandPath);
            }
            catch
            {
                // The ack already prevents a replay, so this is best effort.
            }
        }
    }
}
