using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GK2Trainer
{
    /// <summary>
    /// Builds the state document the desktop app reads.
    ///
    /// Everything here is read through the game's own public API
    /// (MainGame.PlayerData / GetRes / hpComponent / environmentData) - no
    /// reflection, no memory access. Field locations were established by
    /// decompiling Assembly-CSharp.dll; see docs/recon.md.
    /// </summary>
    internal static class Snapshot
    {
        /// <summary>
        /// Resource ids the trainer exposes. They come from PlayerData.Init() and
        /// PlayerData.InitGameResSystems(); more ids can be added the same way.
        /// </summary>
        public static readonly (string Id, string Label)[] KnownResources =
        {
            ("money", "金币"),
            ("tech_red", "红球"),
            ("tech_green", "绿球"),
            ("tech_blue", "蓝球"),
            ("energy", "能量"),
            ("insanity", "疯狂"),
            ("stamina", "体力"),
            ("happiness", "幸福"),
        };

        public static string UnityVersion
        {
            get
            {
                try { return Application.unityVersion; }
                catch { return "?"; }
            }
        }

        public static bool InGame
        {
            get
            {
                try
                {
                    // PlayerData only exists while a save is loaded, which is
                    // exactly when operations are allowed.
                    return MainGame.Instance != null && MainGame.PlayerData != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Reads one player resource; returns null when unavailable.</summary>
        public static float? ReadResource(string id)
        {
            try
            {
                var data = MainGame.PlayerData;
                if (data == null) return null;
                return data.GetRes(id);
            }
            catch (Exception ex)
            {
                Bridge.Log($"read res '{id}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads a resource system's bounds (min/max); max may be -1 when unbounded.</summary>
        public static (float Min, float Max) ReadResourceBounds(string id)
        {
            try
            {
                var system = MainGame.PlayerData?.GetResSystem(id) as GK2GameResSystem;
                if (system == null) return (0f, -1f);
                return (system.Min, system.Max);
            }
            catch
            {
                return (0f, -1f);
            }
        }

        public static JObject Build(string token, long seq, List<string> errors, List<JObject> results)
        {
            var now = 0f;
            try { now = Time.realtimeSinceStartup; } catch { /* ignore */ }

            var state = new JObject
            {
                ["version"] = Bridge.Version,
                ["token"] = token,
                ["seq"] = seq,
                ["ok"] = errors.Count == 0,
                ["inGame"] = InGame,
                ["timestamp"] = Math.Round(now, 2),
                ["errors"] = new JArray(errors),
                ["results"] = new JArray(results),
            };

            var game = new JObject
            {
                ["unity"] = UnityVersion,
            };
            try
            {
                var instance = MainGame.Instance;
                if (instance != null && instance.GameSave != null)
                {
                    game["saveVersion"] = instance.GameSave.SaveVersion.ToString();
                    game["day"] = instance.GameSave.environmentData?.Day ?? -1;
                }
            }
            catch (Exception ex)
            {
                Bridge.Log("game info failed: " + ex.Message);
            }

            state["game"] = game;
            state["targets"] = new JArray { BuildPlayerTarget() };
            return state;
        }

        private static JObject BuildPlayerTarget()
        {
            var target = new JObject
            {
                ["id"] = "player",
                ["name"] = "守墓人",
                ["isHost"] = true,
            };

            var resources = new JObject();
            foreach (var (id, label) in KnownResources)
            {
                var value = ReadResource(id);
                if (value == null) continue;

                var (min, max) = ReadResourceBounds(id);
                resources[id] = new JObject
                {
                    ["label"] = label,
                    ["value"] = Math.Round(value.Value, 2),
                    ["min"] = Math.Round(min, 2),
                    ["max"] = Math.Round(max, 2),
                };
            }
            target["resources"] = resources;

            var vitals = new JObject();
            try
            {
                var hp = MainGame.PlayerData?.hpComponent;
                if (hp != null)
                {
                    vitals["hp"] = hp.Hp;
                    vitals["maxHp"] = hp.MaxHpValue;
                    vitals["immune"] = hp.IsImmuneToDamage;
                }

                var energy = MainGame.PlayerData?.energySystem;
                if (energy != null) vitals["timeWithoutSleep"] = Math.Round(energy.timeWithoutSleep, 2);
            }
            catch (Exception ex)
            {
                Bridge.Log("vitals failed: " + ex.Message);
            }
            target["vitals"] = vitals;

            target["moveSpeed"] = BuildMoveSpeed();
            target["gameSpeed"] = BuildGameSpeed();
            target["perks"] = BuildPerks();
            target["knowledge"] = BuildKnowledge();
            return target;
        }

        /// <summary>Game speed: <c>Time.timeScale</c>, locked while the game is not paused.</summary>
        private static JObject BuildGameSpeed()
        {
            var info = new JObject();
            if (!InGame) return info;   // MainGame.Instance is null in the menu

            try
            {
                info["current"] = Math.Round(UnityEngine.Time.timeScale, 3);
                info["locked"] = Ops.GameSpeedLock.HasValue;
                if (Ops.GameSpeedLock.HasValue) info["lockValue"] = Math.Round(Ops.GameSpeedLock.Value, 3);
                info["paused"] = MainGame.IsGamePaused;
            }
            catch (Exception ex)
            {
                Bridge.LogOnce("gameSpeed", "gameSpeed failed: " + ex.Message);
            }
            return info;
        }

        /// <summary>
        /// Movement speed: the game moves the player by
        /// <c>physicsConfig.speed * SpeedMultiplier</c> (PlayerPhysicalBody.MoveByDirection).
        /// The multiplier is what game states use (attacking halves it), so it is
        /// the safe knob to turn; see Ops for the lock that keeps it applied.
        /// </summary>
        private static JObject BuildMoveSpeed()
        {
            var info = new JObject();
            if (!InGame) return info;

            try
            {
                var body = MainGame.PlayerController?.PhysicalBody;
                if (body == null) return info;

                var baseSpeed = body.PhysicsConfig?.speed ?? 0f;
                var multiplier = body.SpeedMultiplier;

                info["multiplier"] = Math.Round(multiplier, 2);
                info["base"] = Math.Round(baseSpeed, 2);
                info["effective"] = Math.Round(baseSpeed * multiplier, 2);
                info["locked"] = Ops.MoveSpeedLock.HasValue;
                if (Ops.MoveSpeedLock.HasValue) info["lockValue"] = Math.Round(Ops.MoveSpeedLock.Value, 2);
            }
            catch (Exception ex)
            {
                Bridge.LogOnce("moveSpeed", "moveSpeed failed: " + ex.Message);
            }
            return info;
        }

        private static JArray BuildPerks()
        {
            var list = new JArray();
            try
            {
                var perks = MainGame.Instance?.GameSave?.perkSystemData?.activePerks;
                if (perks == null) return list;

                foreach (var perk in perks)
                {
                    try
                    {
                        var id = perk?.Definition?.id;
                        if (!string.IsNullOrEmpty(id)) list.Add(id);
                    }
                    catch
                    {
                        // A perk whose definition is missing is simply skipped.
                    }
                }
            }
            catch (Exception ex)
            {
                Bridge.Log("perks failed: " + ex.Message);
            }
            return list;
        }

        /// <summary>Counts only - the full id lists are large and are fetched on demand.</summary>
        private static JObject BuildKnowledge()
        {
            var info = new JObject();
            try
            {
                var knowledge = MainGame.Instance?.GameSave?.knowledgeSystem;
                if (knowledge == null) return info;

                info["unlockedTechs"] = knowledge.unlockedTechs?.Count ?? 0;
                info["unlockedCrafts"] = knowledge.unlockedCrafts?.Count ?? 0;
                info["unlockedBuildings"] = knowledge.unlockedBuildings?.Count ?? 0;
                info["unlockedTalentIds"] = knowledge.unlockedTalentIds?.Count ?? 0;
                info["totalTechs"] = GameBalance.Me?.techDefs?.Count ?? 0;
                info["totalPerkDefs"] = GameBalance.Me?.perkDefs?.Count ?? 0;
                info["totalItemDefs"] = GameBalance.Me?.itemDefs?.Count ?? 0;
            }
            catch (Exception ex)
            {
                Bridge.Log("knowledge failed: " + ex.Message);
            }
            return info;
        }
    }
}
