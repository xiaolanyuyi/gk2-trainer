using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GK2Trainer
{
    /// <summary>
    /// Graveyard Keeper 2 trainer bridge.
    ///
    /// This plugin is the "game side" of the trainer: it publishes a state
    /// snapshot and applies operations that the desktop app writes into
    /// command.json. It uses only the game's own public API, so it stays stable
    /// across updates as long as those members keep their names.
    /// </summary>
    [BepInPlugin(Guid, "GK2 Trainer Bridge", Bridge.Version)]
    public class TrainerPlugin : BaseUnityPlugin
    {
        public const string Guid = "gk2trainer.bridge";

        /// <summary>How often command.json is checked (seconds).</summary>
        private const float PollInterval = 0.12f;

        /// <summary>The state is rewritten at least this often, so the app can tell we are alive.</summary>
        private const float HeartbeatInterval = 1.0f;

        private static ManualLogSource _log;

        private string _ackToken = "";
        private long _ackSeq;
        private float _lastPoll;
        private float _lastHeartbeat;

        // Kept until the next command so the app can show what happened even if
        // it polls a second later (the heartbeat rewrites the state file).
        private readonly List<string> _lastErrors = new List<string>();
        private readonly List<JObject> _lastResults = new List<JObject>();

        private void Awake()
        {
            _log = Logger;
            Bridge.Initialise(Application.persistentDataPath);
            Bridge.LoadAck(out _ackToken, out _ackSeq);

            Bridge.Log($"plugin loaded (v{Bridge.Version}, unity {Snapshot.UnityVersion})");
            Bridge.Log($"bridge directory: {Bridge.Directory_}");
            Bridge.Log($"ack restored: token='{_ackToken}' seq={_ackSeq}");

            _log.LogInfo($"GK2 Trainer bridge ready: {Bridge.Directory_}");
            WriteState();
        }

        private void Update()
        {
            try
            {
                // Keep trainer-applied values in place (game states overwrite
                // the movement multiplier on their own).
                Ops.ApplyLocks();

                var now = Time.realtimeSinceStartup;

                if (now - _lastPoll >= PollInterval)
                {
                    _lastPoll = now;
                    if (Poll()) return;              // command handled (state already written)
                }

                if (now - _lastHeartbeat >= HeartbeatInterval)
                {
                    _lastHeartbeat = now;
                    WriteState();
                }
            }
            catch (Exception ex)
            {
                Bridge.Log("update loop failed: " + ex);
                Bridge.FlushLog();
            }
        }

        /// <summary>Returns true when a command was executed.</summary>
        private bool Poll()
        {
            var command = Bridge.ReadCommand();
            if (command == null) return false;

            var token = (string)command["token"] ?? "";
            var seq = (long?)command["seq"] ?? 0;

            // Same app instance and already executed? Then it is a replay
            // (the command file survived a savegame reload) - ignore it.
            if (token == _ackToken && seq <= _ackSeq)
            {
                Bridge.Log($"ignoring replayed command (seq={seq})");
                return false;
            }

            var ops = command["ops"] as JArray;
            Bridge.Log($"executing command seq={seq} token={token} ops={ops?.Count ?? 0}");

            _ackToken = token;
            _ackSeq = seq;

            _lastErrors.Clear();
            _lastResults.Clear();
            Ops.Run(command, _lastErrors, _lastResults);

            WriteState();
            Bridge.DeleteCommand();
            return true;
        }

        private void WriteState()
        {
            var state = Snapshot.Build(_ackToken, _ackSeq, _lastErrors, _lastResults);
            Bridge.WriteState(state);
            Bridge.FlushLog();
        }

        private void OnDestroy()
        {
            Bridge.Log("plugin unloaded");
            Bridge.FlushLog();
        }
    }
}
