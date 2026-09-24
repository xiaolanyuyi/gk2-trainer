using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GK2Trainer
{
    /// <summary>
    /// Applies the operations of a command document.
    ///
    /// Every operation returns either null (success), an error string, or a
    /// <see cref="JObject"/> result. One failing operation never stops the rest.
    /// </summary>
    internal static class Ops
    {
        public static void Run(JObject command, List<string> errors, List<JObject> results)
        {
            var ops = command["ops"] as JArray;
            if (ops == null) return;

            var index = 0;
            foreach (var token in ops)
            {
                index++;
                if (token is not JObject op)
                {
                    errors.Add($"operation #{index} is malformed");
                    continue;
                }

                var name = (string)op["op"] ?? "";
                var result = Dispatch(name, op);
                if (result == null) continue;

                if (result is string message) errors.Add(message);
                else if (result is JObject obj) results.Add(obj);
            }
        }

        private static object Dispatch(string name, JObject op)
        {
            try
            {
                switch (name)
                {
                    case "setRes": return SetRes(op);
                    case "addRes": return AddRes(op);
                    case "setHp": return SetHp(op);
                    case "heal": return Heal();
                    case "setImmune": return SetImmune(op);
                    case "setMoveSpeed": return SetMoveSpeed(op);
                    case "clearMoveSpeed": return ClearMoveSpeed();
                    case "setGameSpeed": return SetGameSpeed(op);
                    case "clearGameSpeed": return ClearGameSpeed();
                    case "addPerk": return AddPerk(op);
                    case "removePerk": return RemovePerk(op);
                    case "unlockTech": return UnlockTech(op);
                    case "unlockAllTechs": return UnlockAllTechs();
                    case "addItem": return AddItem(op);
                    case "listIds": return ListIds(op);
                    case "listRes": return ListRes();
                    case "probe": return Probe();
                    case "log": return Log(op);
                    default: return $"unknown operation '{name}'";
                }
            }
            catch (Exception ex)
            {
                return $"{name} failed: {ex.Message}";
            }
        }

        // -------------------------------------------------------- movement speed

        /// <summary>
        /// Requested movement speed multiplier. While set, the plugin rewrites
        /// <c>PlayerPhysicalBody.SpeedMultiplier</c> every frame, because game
        /// states (attacking, climbing, ...) overwrite it on their own.
        /// </summary>
        public static float? MoveSpeedLock { get; private set; }

        /// <summary>
        /// Requested game speed (<c>Time.timeScale</c>). Re-applied every frame as
        /// long as the game is not paused: the game itself sets timeScale to 0 while
        /// frozen (ConsolesManager) and must be allowed to do so.
        /// </summary>
        public static float? GameSpeedLock { get; private set; }

        /// <summary>timeScale that was active before the trainer changed it.</summary>
        private static float _originalTimeScale = 1f;

        /// <summary>Called every frame from the plugin; only does work while something is locked.</summary>
        public static void ApplyLocks()
        {
            if (MoveSpeedLock.HasValue) ApplyMoveSpeedLock();
            if (GameSpeedLock.HasValue) ApplyGameSpeedLock();
        }

        private static void ApplyMoveSpeedLock()
        {
            try
            {
                var body = MainGame.PlayerController?.PhysicalBody;
                if (body == null) return;

                if (Math.Abs(body.SpeedMultiplier - MoveSpeedLock.Value) > 0.01f)
                {
                    body.SpeedMultiplier = MoveSpeedLock.Value;
                }
            }
            catch (Exception ex)
            {
                Bridge.Log("move speed lock failed: " + ex.Message);
            }
        }

        private static void ApplyGameSpeedLock()
        {
            try
            {
                // Never fight a pause: the game freezes by setting timeScale to 0,
                // and the pause menu stops updates without touching it.
                if (Time.timeScale <= 0.0001f) return;
                if (MainGame.IsGamePaused) return;

                if (Math.Abs(Time.timeScale - GameSpeedLock.Value) > 0.001f)
                {
                    Time.timeScale = GameSpeedLock.Value;
                }
            }
            catch (Exception ex)
            {
                Bridge.Log("game speed lock failed: " + ex.Message);
            }
        }

        private static string SetMoveSpeed(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var body = MainGame.PlayerController?.PhysicalBody;
            if (body == null) return "player body not available";

            var value = (float?)op["value"] ?? 1f;
            if (value < 0f) value = 0f;
            if (value > 20f) value = 20f;

            MoveSpeedLock = value;
            body.SpeedMultiplier = value;
            Bridge.Log($"move speed locked to x{value}");
            return null;
        }

        private static string ClearMoveSpeed()
        {
            MoveSpeedLock = null;

            try
            {
                var body = MainGame.PlayerController?.PhysicalBody;
                if (body != null) body.SpeedMultiplier = 1f;
            }
            catch (Exception ex)
            {
                return "clear failed: " + ex.Message;
            }

            Bridge.Log("move speed lock cleared");
            return null;
        }

        private static string SetGameSpeed(JObject op)
        {
            try
            {
                var value = (float?)op["value"] ?? 1f;
                if (value < 0.1f) value = 0.1f;
                if (value > 10f) value = 10f;

                if (!GameSpeedLock.HasValue && Time.timeScale > 0.0001f)
                {
                    _originalTimeScale = Time.timeScale;
                }

                GameSpeedLock = value;
                Time.timeScale = value;
                Bridge.Log($"game speed locked to x{value} (original {_originalTimeScale})");
                return null;
            }
            catch (Exception ex)
            {
                return "setGameSpeed failed: " + ex.Message;
            }
        }

        private static string ClearGameSpeed()
        {
            try
            {
                GameSpeedLock = null;
                if (Time.timeScale > 0.0001f) Time.timeScale = _originalTimeScale;
                Bridge.Log($"game speed lock cleared (restored to {_originalTimeScale})");
                return null;
            }
            catch (Exception ex)
            {
                return "clearGameSpeed failed: " + ex.Message;
            }
        }

        // --------------------------------------------------------------- perks

        private static string AddPerk(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["id"] ?? (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "addPerk needs 'id'";

            var perks = MainGame.Instance.GameSave.perkSystemData;
            if (perks.HasPerk(id)) return null;

            perks.AddPerk(id);
            return null;
        }

        private static string RemovePerk(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["id"] ?? (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "removePerk needs 'id'";

            var perks = MainGame.Instance.GameSave.perkSystemData;
            if (!perks.HasPerk(id)) return null;

            perks.RemovePerk(id);
            return null;
        }

        // ----------------------------------------------------------- knowledge

        private static string UnlockTech(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["id"] ?? (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "unlockTech needs 'id'";

            MainGame.Instance.GameSave.knowledgeSystem.UnlockTech(id, silent: true);
            return null;
        }

        private static JObject UnlockAllTechs()
        {
            var error = RequirePlayer();
            if (error != null) return new JObject { ["op"] = "unlockAllTechs", ["value"] = error };

            var knowledge = MainGame.Instance.GameSave.knowledgeSystem;
            var defs = GameBalance.Me?.techDefs;
            if (defs == null) return new JObject { ["op"] = "unlockAllTechs", ["value"] = "no tech definitions" };

            var unlocked = 0;
            foreach (var def in defs)
            {
                try
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    if (knowledge.IsTechUnlocked(def.id)) continue;
                    knowledge.UnlockTech(def.id, silent: true);
                    unlocked++;
                }
                catch (Exception ex)
                {
                    Bridge.Log($"unlock tech '{def?.id}' failed: {ex.Message}");
                }
            }

            return new JObject
            {
                ["op"] = "unlockAllTechs",
                ["value"] = $"unlocked {unlocked} of {defs.Count} techs",
            };
        }

        // ----------------------------------------------------------- inventory

        private static string AddItem(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["id"] ?? (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "addItem needs 'id'";

            var count = (int?)op["count"] ?? (int?)op["amount"] ?? 1;
            if (count <= 0) return "addItem needs a positive 'count'";

            var inventory = MainGame.PlayerData.inventory;
            if (!inventory.CanAddItemToInventory(id, count))
            {
                return $"inventory cannot take {count} x '{id}' (full, or unknown item id)";
            }

            var item = new Item(id, count);
            if (!inventory.AddItemToInventory(item)) return $"AddItemToInventory('{id}') failed";
            return null;
        }

        // ------------------------------------------------------------- id lists

        /// <summary>
        /// Returns the game's own id lists for the UI (item / perk / tech / craft /
        /// building / talent). Optional 'filter' and 'limit' keep the payload small.
        /// </summary>
        private static JObject ListIds(JObject op)
        {
            var kind = ((string)op["kind"] ?? "item").ToLowerInvariant();
            var filter = ((string)op["filter"] ?? "").ToLowerInvariant();
            var limit = (int?)op["limit"] ?? 400;

            var all = new List<string>();
            var balance = GameBalance.Me;
            if (balance == null)
            {
                return new JObject { ["op"] = "listIds", ["value"] = "game balance not loaded" };
            }

            switch (kind)
            {
                case "item": foreach (var def in balance.itemDefs) all.Add(def?.id); break;
                case "perk": foreach (var def in balance.perkDefs) all.Add(def?.id); break;
                case "tech": foreach (var def in balance.techDefs) all.Add(def?.id); break;
                case "craft": foreach (var def in balance.craftDefs) all.Add(def?.id); break;
                case "building": foreach (var def in balance.buildingDefs) all.Add(def?.id); break;
                case "talent": foreach (var def in balance.talentDefs) all.Add(def?.id); break;
                default: return new JObject { ["op"] = "listIds", ["value"] = $"unknown kind '{kind}'" };
            }

            var ids = new JArray();
            var total = 0;
            var matched = 0;
            foreach (var id in all)
            {
                if (string.IsNullOrEmpty(id)) continue;
                total++;
                if (filter.Length > 0 && !id.ToLowerInvariant().Contains(filter)) continue;
                matched++;
                if (ids.Count < limit) ids.Add(id);
            }

            return new JObject
            {
                ["op"] = "listIds",
                ["value"] = $"kind={kind} matched={matched} total={total}",
                ["ids"] = ids,
            };
        }

        // ------------------------------------------------------------ resources

        private static string RequirePlayer()
        {
            return Snapshot.InGame ? null : "not in game (load a save first)";
        }

        private static string SetRes(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "setRes needs 'name'";

            var value = (float?)op["value"] ?? 0f;
            MainGame.PlayerData.SetRes(id, value);
            return null;
        }

        private static string AddRes(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var id = (string)op["name"];
            if (string.IsNullOrEmpty(id)) return "addRes needs 'name'";

            var amount = (float?)op["amount"] ?? 0f;
            MainGame.PlayerData.AddRes(id, amount);
            return null;
        }

        private static JObject ListRes()
        {
            var data = new JObject();
            foreach (var (id, label) in Snapshot.KnownResources)
            {
                var value = Snapshot.ReadResource(id);
                if (value == null) continue;

                var (min, max) = Snapshot.ReadResourceBounds(id);
                data[id] = new JObject
                {
                    ["label"] = label,
                    ["value"] = Math.Round(value.Value, 2),
                    ["min"] = Math.Round(min, 2),
                    ["max"] = Math.Round(max, 2),
                };
            }

            return new JObject { ["op"] = "listRes", ["value"] = data.ToString(Newtonsoft.Json.Formatting.None) };
        }

        // --------------------------------------------------------------- vitals

        private static string SetHp(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var hp = MainGame.PlayerData.hpComponent;
            if (hp == null) return "no hp component";

            var value = (int?)op["value"] ?? 0;
            hp.Hp = value;                       // the setter clamps to >= 0 and handles death
            return null;
        }

        private static string Heal()
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var hp = MainGame.PlayerData.hpComponent;
            if (hp == null) return "no hp component";

            hp.Hp = hp.MaxHpValue;
            return null;
        }

        /// <summary>Toggles HPComponent.IsImmuneToDamage (the game's own flag).</summary>
        private static string SetImmune(JObject op)
        {
            var error = RequirePlayer();
            if (error != null) return error;

            var hp = MainGame.PlayerData.hpComponent;
            if (hp == null) return "no hp component";

            hp.IsImmuneToDamage = (bool?)op["value"] ?? false;
            return null;
        }

        // ---------------------------------------------------------------- debug

        private static JObject Probe()
        {
            var info = new JObject
            {
                ["op"] = "probe",
                ["value"] = $"inGame={Snapshot.InGame} unity={Snapshot.UnityVersion} pid={System.Diagnostics.Process.GetCurrentProcess().Id}",
            };

            var systems = new JObject();
            foreach (var (id, _) in Snapshot.KnownResources)
            {
                try
                {
                    var system = MainGame.PlayerData?.GetResSystem(id) as GK2GameResSystem;
                    if (system == null) continue;
                    systems[id] = new JObject
                    {
                        ["min"] = Math.Round(system.Min, 2),
                        ["max"] = Math.Round(system.Max, 2),
                        ["enough10"] = system.IsEnoughValue(10f),
                        ["canAdd"] = system.CanAddValue(10f),
                    };
                }
                catch (Exception ex)
                {
                    systems[id] = ex.Message;
                }
            }

            info["systems"] = systems;
            return info;
        }

        private static string Log(JObject op)
        {
            Bridge.Log("[app] " + ((string)op["message"] ?? ""));
            return null;
        }
    }
}
