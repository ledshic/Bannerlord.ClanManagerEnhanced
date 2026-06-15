using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    public class ClanManagementBehavior : CampaignBehaviorBase
    {
        private const double BlockedArmyNoticeCooldownSeconds = 30.0;

        private static DateTime _lastBlockedArmyNoticeUtc = DateTime.MinValue;
        private static bool _createPartyMethodResolved;
        private static MethodInfo? _createPartyMethod;

        // Cache for debug logging enabled state to avoid repeated null checks
        private static bool? _debugLoggingEnabled = null;
        private static DateTime _lastSettingsCheckUtc = DateTime.MinValue;
        private static DateTime _lastMessageDisplayUtc = DateTime.MinValue;
        private const double DebugMessageDisplayCooldownSeconds = 5.0; // Show at most one message per 5 seconds

        /// <summary>
        /// Output debug log if enabled in settings
        /// Uses file logging (works without Debug Mode) and optional in-game message display
        /// </summary>
        private static void DebugLog(string message)
        {
            // Refresh settings cache every 30 seconds in case they change
            var now = DateTime.UtcNow;
            if (_debugLoggingEnabled == null || (now - _lastSettingsCheckUtc).TotalSeconds > 30)
            {
                try
                {
                    var settings = ClanManagerSettings.Instance;
                    _debugLoggingEnabled = settings?.EnableDebugLogging ?? false;
                    _lastSettingsCheckUtc = now;
                }
                catch
                {
                    _debugLoggingEnabled = false;
                }
            }

            if (_debugLoggingEnabled == true)
            {
                // Display debug messages in-game
                DebugNotify(message);
                // Also try Debug.Print for Debug Mode compatibility
                Debug.Print($"[ClanManagerEnhanced.Debug] {message}");
            }
        }

        /// <summary>
        /// Display important debug messages in-game as notifications
        /// Useful when Debug Mode is not enabled
        /// </summary>
        private static void DebugNotify(string message, bool isImportant = false)
        {
            try
            {
                // Rate limit notifications to avoid spam
                var now = DateTime.UtcNow;
                if (!isImportant && (now - _lastMessageDisplayUtc).TotalSeconds < DebugMessageDisplayCooldownSeconds)
                {
                    return;
                }

                _lastMessageDisplayUtc = now;

                var text = new TextObject(message);
                InformationManager.DisplayMessage(new InformationMessage(
                    text.ToString(),
                    isImportant ? Colors.Red : Colors.White));
            }
            catch
            {
                // Silently fail
            }
        }

        private static readonly MethodInfo? LeaveArmyMethod = typeof(MobileParty).GetMethod(
            "LeaveArmy",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        private static readonly MethodInfo? RemovePartyMethod = typeof(Army).GetMethod(
            "RemoveParty",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(MobileParty) },
            null);

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTick()
        {
            var settings = ClanManagerSettings.Instance;
            if (settings == null || !settings.EnableMod)
            {
                return;
            }

            DebugLog("=== Daily Tick Started ===");
            DebugLog($"Mod Enabled: {settings.EnableMod}");
            DebugLog($"Debug Logging Enabled: {settings.EnableDebugLogging}");

            if (!settings.AllowPlayerClanPartiesJoinExternalArmies)
            {
                DebugLog("Enforcing external army restriction...");
                EnforceExternalArmyRestriction(settings);
            }

            if (settings.AutoCreatePartyForIdleClanMembers)
            {
                DebugLog("Running auto-create party for idle clan members...");
                AutoCreatePartyForIdleClanMembers(settings);
            }

            if (settings.AutoReinforceParties)
            {
                DebugLog("Running auto-reinforce parties...");
                ReinforceLowStrengthParties(settings);
            }

            if (settings.AutoTransferExcessPrisoners)
            {
                DebugLog("Running auto-transfer excess prisoners...");
                TransferExcessPrisonersTodungeons(settings);
            }

            if (settings.ShowNotifications)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DAILY_TICK}ClanManagerEnhanced daily check executed.").ToString()));
            }

            DebugLog("=== Daily Tick Completed ===");
        }

        private void AutoCreatePartyForIdleClanMembers(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                DebugLog("PlayerClan is null, skipping auto party creation");
                return;
            }

            DebugLog($"=== AutoCreatePartyForIdleClanMembers START ===");
            DebugLog($"[PHASE 1] Player Clan: {playerClan.Name}, Tier: {playerClan.Tier}");

            if (!HasAvailableClanPartySlot(playerClan))
            {
                var currentPartyCount = MobileParty.All.Count(p => p?.LeaderHero?.Clan == playerClan && !p.IsMainParty);
                DebugLog($"[PHASE 1] ❌ No available party slots. Current parties: {currentPartyCount}");
                DebugLog($"=== AutoCreatePartyForIdleClanMembers END (No party slots) ===");
                return;
            }

            DebugLog($"[PHASE 1] ✓ Available party slots exist");

            // Collect all alive heroes in the clan for logging
            var allClanHeroes = Hero.AllAliveHeroes.Where(h => h.Clan == playerClan).ToList();
            DebugLog($"[PHASE 2] Total clan members (alive): {allClanHeroes.Count}");
            DebugLog($"[PHASE 2] Total all heroes (alive): {Hero.AllAliveHeroes.Count()}");

            int idleCount = 0;
            int createdCount = 0;
            int notIdleCount = 0;
            var idleHeroesList = new List<Hero>();
            var notIdleReasons = new Dictionary<string, int>(); // Count reasons for non-idle heroes

            DebugLog($"[PHASE 3] Starting hero iteration...");

            foreach (var hero in Hero.AllAliveHeroes)
            {
                var checkResult = IsIdleTavernClanMemberWithDetails(hero, playerClan);

                if (!checkResult.isIdle)
                {
                    notIdleCount++;
                    // Count reasons
                    if (!notIdleReasons.ContainsKey(checkResult.reason))
                    {
                        notIdleReasons[checkResult.reason] = 0;
                    }
                    notIdleReasons[checkResult.reason]++;

                    // Log only clan members' rejection reasons for clarity
                    if (hero.Clan == playerClan)
                    {
                        DebugLog($"[PHASE 3] [NOT_IDLE] {hero.Name}: {checkResult.reason}");
                    }
                    continue;
                }

                idleCount++;
                idleHeroesList.Add(hero);
                DebugLog($"[PHASE 3] [IDLE_FOUND] {hero.Name} in {hero.CurrentSettlement?.Name}");

                if (!HasAvailableClanPartySlot(playerClan))
                {
                    DebugLog($"[PHASE 4] ⚠️ Party slots filled. Found {idleCount} idle members, created {createdCount} parties. Stopping iteration.");
                    break;
                }

                DebugLog($"[PHASE 4] [ATTEMPT_CREATE] Creating party for {hero.Name}...");
                if (TryCreatePartyForHero(hero))
                {
                    createdCount++;
                    DebugLog($"[PHASE 4] ✓ [SUCCESS] Created party for {hero.Name}");
                }
                else
                {
                    DebugLog($"[PHASE 4] ❌ [FAILED] Failed to create party for {hero.Name}");
                }
            }

            // Log detailed summary
            DebugLog("=== DETAILED SUMMARY ===");
            DebugLog($"[SUMMARY] Total heroes checked: {Hero.AllAliveHeroes.Count()}");
            DebugLog($"[SUMMARY] Clan members (alive): {allClanHeroes.Count}");
            DebugLog($"[SUMMARY] Idle clan members found: {idleCount}");
            DebugLog($"[SUMMARY] Non-idle heroes checked: {notIdleCount}");
            DebugLog($"[SUMMARY] Parties created: {createdCount}");

            // Log breakdown of non-idle reasons
            DebugLog("=== NON-IDLE BREAKDOWN ===");
            foreach (var kvp in notIdleReasons.OrderByDescending(x => x.Value))
            {
                DebugLog($"[BREAKDOWN] {kvp.Key}: {kvp.Value} heroes");
            }

            if (idleHeroesList.Count > 0)
            {
                DebugLog($"[IDLE_LIST] Idle members: {string.Join(", ", idleHeroesList.Select(h => h.Name.ToString()))}");
            }

            DebugLog($"=== AutoCreatePartyForIdleClanMembers END ===");

            if (settings.ShowNotifications)
            {
                if (idleCount > 0)
                {
                    var idleText = new TextObject("{=CME_IDLE_MEMBERS_FOUND}Found {COUNT} idle clan member(s) in towns.");
                    idleText.SetTextVariable("COUNT", idleCount);
                    InformationManager.DisplayMessage(new InformationMessage(idleText.ToString()));
                }

                if (createdCount > 0)
                {
                    var createdText = new TextObject("{=CME_AUTO_CREATE_PARTY_CREATED}Automatically created {COUNT} clan party(ies) from idle tavern members.");
                    createdText.SetTextVariable("COUNT", createdCount);
                    InformationManager.DisplayMessage(new InformationMessage(createdText.ToString()));
                }
            }
        }

        private void EnforceExternalArmyRestriction(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                DebugLog("PlayerClan is null, skipping external army enforcement");
                return;
            }

            int blockedCount = 0;
            string? blockedPartyName = null;

            foreach (var party in MobileParty.All)
            {
                if (!ShouldCheckParty(party, playerClan))
                {
                    continue;
                }

                var army = party.Army;
                if (army == null)
                {
                    continue;
                }

                // Allowed case: player explicitly calls the party into player's own army.
                if (army.LeaderParty != null && army.LeaderParty.IsMainParty)
                {
                    DebugLog($"Party {party.Name} is in player's own army, allowing");
                    continue;
                }

                if (TryForceLeaveArmy(party, army))
                {
                    blockedCount++;
                    blockedPartyName ??= party.Name?.ToString();
                    DebugLog($"Forced party {party.Name} to leave external army");

                    if (settings.ShowNotifications)
                    {
                        // notification already handled
                    }
                }
            }

            DebugLog($"External army enforcement: blocked {blockedCount} parties");

            if (blockedCount > 0 && settings.ShowNotifications && ShouldShowBlockedArmyNotice())
            {
                var text = new TextObject("{=CME_BLOCK_EXT_ARMY_JOIN}Blocked {COUNT} player clan party(ies) from staying in a non-player army.");
                text.SetTextVariable("COUNT", blockedCount);

                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
                _lastBlockedArmyNoticeUtc = DateTime.UtcNow;
            }
        }

        private static bool ShouldShowBlockedArmyNotice()
        {
            var now = DateTime.UtcNow;
            return (now - _lastBlockedArmyNoticeUtc).TotalSeconds >= BlockedArmyNoticeCooldownSeconds;
        }

        private static bool ShouldCheckParty(MobileParty? party, Clan playerClan)
        {
            if (party == null || party.IsMainParty || party.LeaderHero == null)
            {
                return false;
            }

            if (party.LeaderHero.Clan != playerClan)
            {
                return false;
            }

            if (party.IsDisbanding || party.MapEvent != null)
            {
                return false;
            }

            return true;
        }

        private static (bool isIdle, string reason) IsIdleTavernClanMemberWithDetails(Hero? hero, Clan playerClan)
        {
            if (hero == null)
            {
                return (false, "hero is null");
            }

            if (hero == Hero.MainHero)
            {
                return (false, "is main hero");
            }

            if (hero.Clan != playerClan)
            {
                return (false, $"clan mismatch: hero.Clan={hero.Clan?.Name?.ToString() ?? "null"}, playerClan={playerClan.Name}");
            }

            if (hero.IsDead)
            {
                return (false, "hero is dead");
            }

            if (hero.IsChild)
            {
                return (false, "hero is a child");
            }

            if (hero.IsPrisoner)
            {
                return (false, "hero is a prisoner");
            }

            if (hero.PartyBelongedTo != null)
            {
                return (false, $"already in party: {hero.PartyBelongedTo.Name}");
            }

            if (hero.GovernorOf != null)
            {
                return (false, $"is governor of: {hero.GovernorOf.Name}");
            }

            var settlement = hero.CurrentSettlement;
            if (settlement == null)
            {
                return (false, "current settlement is null");
            }

            if (!settlement.IsTown)
            {
                return (false, $"settlement is not a town: {settlement.Name} (IsVillage={settlement.IsVillage}, IsHideout={settlement.IsHideout})");
            }

            // Check StayingInSettlement property for compatibility across game versions
            if (TryGetBoolProperty(hero, "StayingInSettlement", out var stayingInSettlement))
            {
                if (!stayingInSettlement)
                {
                    return (false, "StayingInSettlement=false");
                }
            }

            return (true, $"IDLE in town {settlement.Name}");
        }

        private static bool IsIdleTavernClanMember(Hero? hero, Clan playerClan)
        {
            var (isIdle, reason) = IsIdleTavernClanMemberWithDetails(hero, playerClan);

            if (!isIdle && hero != null && hero != Hero.MainHero)
            {
                DebugLog($"[FILTERED] {hero.Name}: {reason}");
            }

            return isIdle;
        }

        private static bool HasAvailableClanPartySlot(Clan playerClan)
        {
            if (!TryGetClanPartyLimit(playerClan, out var partyLimit))
            {
                DebugLog($"Could not determine party limit for clan {playerClan.Name}, assuming unlimited");
                return true;
            }

            var currentPartyCount = MobileParty.All.Count(p =>
                p != null &&
                !p.IsMainParty &&
                !p.IsDisbanding &&
                p.LeaderHero != null &&
                p.LeaderHero.Clan == playerClan);

            bool hasSlot = currentPartyCount < partyLimit;
            DebugLog($"Party slot check for {playerClan.Name}: {currentPartyCount}/{partyLimit} slots used");

            return hasSlot;
        }

        private static bool TryGetClanPartyLimit(Clan playerClan, out int partyLimit)
        {
            partyLimit = 0;

            var campaign = Campaign.Current;
            if (campaign?.Models == null)
            {
                return false;
            }

            var clanTierModel = campaign.Models.ClanTierModel;
            if (clanTierModel == null)
            {
                return false;
            }

            // Prefer well-known names, then generic party-limit name scan.
            if (TryInvokeIntMethod(clanTierModel, "GetPartyLimitForTier", new object[] { playerClan.Tier }, out partyLimit) ||
                TryInvokeIntMethod(clanTierModel, "GetPartyLimit", new object[] { playerClan }, out partyLimit) ||
                TryInvokeIntMethod(clanTierModel, "CalculatePartyLimitForTier", new object[] { playerClan.Tier }, out partyLimit))
            {
                return partyLimit > 0;
            }

            foreach (var method in clanTierModel.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name.IndexOf("Party", StringComparison.OrdinalIgnoreCase) < 0 ||
                    method.Name.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) < 0 ||
                    method.ReturnType != typeof(int))
                {
                    continue;
                }

                if (TryInvokeIntMethodWithKnownArgs(clanTierModel, method, playerClan, out partyLimit))
                {
                    return partyLimit > 0;
                }
            }

            return false;
        }

        private static bool TryCreatePartyForHero(Hero hero)
        {
            try
            {
                var method = GetOrResolveCreatePartyMethod();
                if (method == null)
                {
                    DebugLog($"No create party method found for hero {hero.Name}");
                    return false;
                }

                var args = BuildArgumentsForMethod(method, hero);
                if (args == null)
                {
                    DebugLog($"Failed to build arguments for create party method for hero {hero.Name}");
                    return false;
                }

                DebugLog($"Attempting to create party for hero {hero.Name} using method {method.Name}");
                var result = method.Invoke(null, args);
                return result is bool created ? created : true;
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] Failed to create clan party for {hero.Name}: {ex}");
                Debug.Print($"[ClanManagerEnhanced] Failed to create clan party for {hero.Name}: {ex}");
                DebugLog($"Exception creating party for hero {hero.Name}: {ex.Message}");
                return false;
            }
        }

        private static MethodInfo? GetOrResolveCreatePartyMethod()
        {
            if (_createPartyMethodResolved)
            {
                return _createPartyMethod;
            }

            _createPartyMethodResolved = true;

            var campaignAssembly = typeof(Campaign).Assembly;
            var candidateTypeNames = new[]
            {
                "TaleWorlds.CampaignSystem.Actions.CreateNewMobilePartyAction",
                "TaleWorlds.CampaignSystem.Actions.CreateNewClanMobilePartyAction",
                "TaleWorlds.CampaignSystem.Actions.CreateClanPartyAction"
            };

            foreach (var typeName in candidateTypeNames)
            {
                var candidateType = campaignAssembly.GetType(typeName, false);
                var candidateMethod = FindUsableCreatePartyApplyMethod(candidateType);
                if (candidateMethod != null)
                {
                    _createPartyMethod = candidateMethod;
                    return _createPartyMethod;
                }
            }

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loadedAssemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null ||
                        type.Name.IndexOf("Create", StringComparison.OrdinalIgnoreCase) < 0 ||
                        type.Name.IndexOf("Party", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var candidateMethod = FindUsableCreatePartyApplyMethod(type);
                    if (candidateMethod != null)
                    {
                        _createPartyMethod = candidateMethod;
                        return _createPartyMethod;
                    }
                }
            }

            DebugLog("[ERROR] No compatible create-party action method found.");
            Debug.Print("[ClanManagerEnhanced] No compatible create-party action method found.");
            return null;
        }

        private static MethodInfo? FindUsableCreatePartyApplyMethod(Type? candidateType)
        {
            if (candidateType == null)
            {
                return null;
            }

            foreach (var method in candidateType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "Apply", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 0 || !parameters.Any(p => p.ParameterType == typeof(Hero)))
                {
                    continue;
                }

                var args = BuildArgumentsForMethod(method, Hero.MainHero);
                if (args != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static object?[]? BuildArgumentsForMethod(MethodInfo method, Hero hero)
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var paramType = parameter.ParameterType;

                if (paramType == typeof(Hero))
                {
                    args[i] = hero;
                    continue;
                }

                if (paramType == typeof(Clan))
                {
                    args[i] = hero.Clan;
                    continue;
                }

                if (paramType == typeof(Settlement))
                {
                    args[i] = hero.CurrentSettlement;
                    continue;
                }

                if (paramType == typeof(bool))
                {
                    args[i] = false;
                    continue;
                }

                if (paramType == typeof(int))
                {
                    args[i] = 0;
                    continue;
                }

                if (paramType == typeof(float))
                {
                    args[i] = 0f;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                    continue;
                }

                return null;
            }

            return args;
        }

        private static bool TryInvokeIntMethod(object instance, string methodName, object[] args, out int result)
        {
            result = 0;
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return false;
            }

            try
            {
                var invokeResult = method.Invoke(instance, args);
                if (invokeResult is int intValue)
                {
                    result = intValue;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryInvokeIntMethodWithKnownArgs(object instance, MethodInfo method, Clan playerClan, out int result)
        {
            result = 0;
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(Clan))
                {
                    args[i] = playerClan;
                }
                else if (paramType == typeof(int))
                {
                    args[i] = playerClan.Tier;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                }
                else
                {
                    return false;
                }
            }

            try
            {
                var invokeResult = method.Invoke(instance, args);
                if (invokeResult is int intValue)
                {
                    result = intValue;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryGetBoolProperty(object instance, string propertyName, out bool value)
        {
            value = false;
            var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || prop.PropertyType != typeof(bool))
            {
                return false;
            }

            try
            {
                value = (bool)(prop.GetValue(instance) ?? false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryForceLeaveArmy(MobileParty party, Army army)
        {
            try
            {
                if (LeaveArmyMethod != null)
                {
                    LeaveArmyMethod.Invoke(party, null);
                    return true;
                }

                if (RemovePartyMethod != null)
                {
                    RemovePartyMethod.Invoke(army, new object[] { party });
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] Failed to remove party from army: {ex}");
                Debug.Print($"[ClanManagerEnhanced] Failed to remove party from army: {ex}");
            }

            return false;
        }

        private void ReinforceLowStrengthParties(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                DebugLog("PlayerClan is null, skipping reinforcement");
                return;
            }

            var lowStrengthParties = MobileParty.All
                .Where(p => ShouldCheckParty(p, playerClan) && IsPartyBelowStrengthThreshold(p, settings))
                .ToList();

            DebugLog($"Found {lowStrengthParties.Count} low-strength parties out of {MobileParty.All.Count(p => ShouldCheckParty(p, playerClan))} clan parties");

            if (lowStrengthParties.Count == 0)
            {
                return;
            }

            var castles = Settlement.All
                .Where(s => s.IsVillage == false && s.IsHideout == false && s.OwnerClan == playerClan && s.Town != null)
                .ToList();

            var overgarrisonedCastles = castles
                .Where(s => IsCastleOvergarrisoned(s, settings))
                .ToList();

            DebugLog($"Found {overgarrisonedCastles.Count} overgarrisoned castles out of {castles.Count} total clan castles");

            if (overgarrisonedCastles.Count == 0)
            {
                DebugLog("No overgarrisoned castles available for troop extraction");
                return;
            }

            int totalReinforced = 0;
            foreach (var party in lowStrengthParties)
            {
                if (IsPartyBelowStrengthThreshold(party, settings))
                {
                    int reinforcedCount = ExtractAndReinforceParty(party, overgarrisonedCastles, settings);
                    totalReinforced += reinforcedCount;
                    DebugLog($"Reinforced party {party.Name} with {reinforcedCount} troops");
                }
            }

            DebugLog($"Reinforcement completed: reinforced {totalReinforced} total troops");

            if (totalReinforced > 0 && settings.ShowNotifications)
            {
                var text = new TextObject("{=CME_REINFORCE_SUCCESS}Reinforced party troops with {COUNT} soldiers from castles.");
                text.SetTextVariable("COUNT", totalReinforced);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
        }

        private void TransferExcessPrisonersTodungeons(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                DebugLog("PlayerClan is null, skipping prisoner transfer");
                return;
            }

            var overloadedParties = MobileParty.All
                .Where(p => ShouldCheckParty(p, playerClan) && IsPartyOverloadedWithPrisoners(p, settings))
                .ToList();

            DebugLog($"Found {overloadedParties.Count} overloaded parties out of {MobileParty.All.Count(p => ShouldCheckParty(p, playerClan))} clan parties");

            if (overloadedParties.Count == 0)
            {
                return;
            }

            var castles = Settlement.All
                .Where(s => s.IsVillage == false && s.IsHideout == false && s.OwnerClan == playerClan && s.Town != null)
                .ToList();

            DebugLog($"Found {castles.Count} clan castles for prisoner transfer");

            if (castles.Count == 0)
            {
                DebugLog("No castles available for prisoner transfer");
                return;
            }

            int totalTransferred = 0;
            foreach (var party in overloadedParties)
            {
                if (IsPartyOverloadedWithPrisoners(party, settings))
                {
                    int transferredCount = TransferPrisonersToClosestCastle(party, castles);
                    totalTransferred += transferredCount;
                    DebugLog($"Transferred {transferredCount} prisoners from party {party.Name}");
                }
            }

            DebugLog($"Prisoner transfer completed: transferred {totalTransferred} total prisoners");

            if (totalTransferred > 0 && settings.ShowNotifications)
            {
                var text = new TextObject("{=CME_TRANSFER_PRISONERS}Transferred {COUNT} prisoners to castle dungeons.");
                text.SetTextVariable("COUNT", totalTransferred);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
        }

        private static bool IsPartyBelowStrengthThreshold(MobileParty? party, ClanManagerSettings settings)
        {
            if (party == null)
            {
                return false;
            }

            var maxStrength = party.Party?.PartySizeLimit ?? 0;
            if (maxStrength <= 0)
            {
                return false;
            }

            var currentStrength = party.MemberRoster?.TotalHealthyCount ?? 0;
            var threshold = (int)Math.Ceiling(maxStrength * settings.PartyTroopThresholdPercent / 100f);

            return currentStrength < threshold;
        }

        private static bool IsCastleOvergarrisoned(Settlement? settlement, ClanManagerSettings settings)
        {
            if (settlement?.Town == null)
            {
                return false;
            }

            var maxGarrison = settlement.Town.GarrisonParty?.Party?.PartySizeLimit ?? 0;
            if (maxGarrison <= 0)
            {
                return false;
            }

            var currentGarrison = settlement.Town.GarrisonParty?.MemberRoster?.TotalHealthyCount ?? 0;
            var overgarrisonThreshold = (int)Math.Ceiling(maxGarrison * settings.CastleOvergarrisonThresholdPercent / 100f);

            return currentGarrison > overgarrisonThreshold;
        }

        private static bool IsPartyOverloadedWithPrisoners(MobileParty? party, ClanManagerSettings settings)
        {
            if (party == null)
            {
                return false;
            }

            var maxPrisoners = party.Party?.PartySizeLimit ?? 0;
            if (maxPrisoners <= 0)
            {
                return false;
            }

            var currentPrisoners = party.PrisonRoster?.TotalHealthyCount ?? 0;
            var threshold = (int)Math.Ceiling(maxPrisoners * settings.PartyPrisonerThresholdPercent / 100f);

            return currentPrisoners > threshold;
        }

        private static int ExtractAndReinforceParty(MobileParty targetParty, List<Settlement> castles, ClanManagerSettings settings)
        {
            try
            {
                var maxCapacity = targetParty.Party?.PartySizeLimit ?? 0;
                if (maxCapacity <= 0)
                {
                    return 0;
                }

                var currentStrength = targetParty.MemberRoster?.TotalHealthyCount ?? 0;
                var extractAmount = (int)Math.Ceiling(maxCapacity * settings.ReinforcePercentOfPartyCapacity / 100f);
                var availableSpace = maxCapacity - currentStrength;
                var amountToAdd = Math.Min(extractAmount, availableSpace);

                if (amountToAdd <= 0)
                {
                    return 0;
                }

                int totalExtracted = 0;
                foreach (var castle in castles)
                {
                    if (totalExtracted >= amountToAdd)
                    {
                        break;
                    }

                    var garrisonParty = castle.Town?.GarrisonParty;
                    if (garrisonParty?.MemberRoster == null)
                    {
                        continue;
                    }

                    int remainingNeeded = amountToAdd - totalExtracted;
                    int extracted = ExtractTroopsFromGarrison(garrisonParty, targetParty, remainingNeeded);
                    totalExtracted += extracted;
                }

                return totalExtracted;
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] Failed to reinforce party: {ex}");
                Debug.Print($"[ClanManagerEnhanced] Failed to reinforce party: {ex}");
                return 0;
            }
        }

        private static int ExtractTroopsFromGarrison(MobileParty garrisonParty, MobileParty targetParty, int maxExtract)
        {
            try
            {
                var garrison = garrisonParty.MemberRoster;
                var targetRoster = targetParty.MemberRoster;

                if (garrison == null || targetRoster == null)
                {
                    return 0;
                }

                int extracted = 0;
                var troopsByTier = new SortedDictionary<int, List<CharacterObject>>();

                // Group troops by tier (high to low)
                for (int i = garrison.Count - 1; i >= 0; i--)
                {
                    var character = garrison.GetCharacterAtIndex(i);
                    if (character == null)
                    {
                        continue;
                    }

                    int tier = character.Tier;
                    if (!troopsByTier.ContainsKey(-tier))
                    {
                        troopsByTier[-tier] = new List<CharacterObject>();
                    }

                    var count = garrison.GetElementNumber(i);
                    for (int j = 0; j < count && extracted < maxExtract; j++)
                    {
                        troopsByTier[-tier].Add(character);
                        extracted++;
                    }
                }

                // Transfer highest tier first
                foreach (var tierGroup in troopsByTier)
                {
                    foreach (var character in tierGroup.Value)
                    {
                        garrison.AddToCounts(character, -1);
                        targetRoster.AddToCounts(character, 1);
                    }
                }

                return extracted;
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] Failed to extract troops from garrison: {ex}");
                Debug.Print($"[ClanManagerEnhanced] Failed to extract troops from garrison: {ex}");
                return 0;
            }
        }

        private static int TransferPrisonersToClosestCastle(MobileParty party, List<Settlement> castles)
        {
            try
            {
                if (party.PrisonRoster?.Count == 0)
                {
                    return 0;
                }

                // Find castle with available dungeon space
                var bestCastle = castles.FirstOrDefault();
                if (bestCastle?.Town?.GarrisonParty == null)
                {
                    return 0;
                }

                int transferred = 0;
                var prisonRoster = party.PrisonRoster;
                var garrisonParty = bestCastle.Town.GarrisonParty;
                var dungeonRoster = garrisonParty.PrisonRoster;

                if (dungeonRoster == null)
                {
                    return 0;
                }

                var dungeonCapacity = garrisonParty.Party?.PartySizeLimit ?? 300; // Fallback estimate

                var currentDungeonCount = dungeonRoster.TotalHealthyCount;
                var availableSpace = dungeonCapacity - currentDungeonCount;

                if (availableSpace <= 0)
                {
                    return 0;
                }

                for (int i = prisonRoster.Count - 1; i >= 0 && transferred < availableSpace; i--)
                {
                    var character = prisonRoster.GetCharacterAtIndex(i);
                    if (character == null)
                    {
                        continue;
                    }

                    var count = prisonRoster.GetElementNumber(i);
                    int toTransfer = Math.Min(count, availableSpace - transferred);

                    prisonRoster.AddToCounts(character, -toTransfer);
                    dungeonRoster.AddToCounts(character, toTransfer);
                    transferred += toTransfer;
                }

                return transferred;
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] Failed to transfer prisoners: {ex}");
                Debug.Print($"[ClanManagerEnhanced] Failed to transfer prisoners: {ex}");
                return 0;
            }
        }
    }
}
