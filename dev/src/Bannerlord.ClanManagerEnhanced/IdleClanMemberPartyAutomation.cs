using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    internal sealed class IdleClanMemberPartyAutomation
    {
        private static readonly MethodInfo? CreateLordPartyMethod = typeof(LordPartyComponent).GetMethod(
            "CreateLordParty",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(string),
                typeof(Hero),
                typeof(CampaignVec2),
                typeof(float),
                typeof(Settlement),
                typeof(Hero)
            },
            null);

        internal void AutoCreatePartyForIdleClanMembers(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                CmeDiagnostics.DebugLog("PlayerClan is null, skipping auto party creation");
                return;
            }

            CmeDiagnostics.DebugLog("=== AutoCreatePartyForIdleClanMembers START ===");
            CmeDiagnostics.DebugLog($"[PHASE 1] Player Clan: {playerClan.Name}, Tier: {playerClan.Tier}");

            if (!HasAvailableClanPartySlot(playerClan))
            {
                var currentPartyCount = GetCurrentClanPartyCount(playerClan);
                CmeDiagnostics.DebugLog($"[PHASE 1] No available party slots. Current parties (including main): {currentPartyCount}");
                CmeDiagnostics.DebugLog("=== AutoCreatePartyForIdleClanMembers END (No party slots) ===");
                return;
            }

            var allClanHeroes = Hero.AllAliveHeroes.Where(h => h.Clan == playerClan).ToList();
            CmeDiagnostics.DebugLog($"[PHASE 2] Total clan members (alive): {allClanHeroes.Count}");

            var idleCount = 0;
            var createdCount = 0;
            var joinedPlayerPartyCount = 0;
            var notIdleCount = 0;
            var idleHeroesList = new List<Hero>();
            var notIdleReasons = new Dictionary<string, int>();

            foreach (var hero in Hero.AllAliveHeroes)
            {
                var checkResult = IsIdleTavernClanMemberWithDetails(hero, playerClan);
                if (!checkResult.isIdle)
                {
                    notIdleCount++;
                    if (!notIdleReasons.ContainsKey(checkResult.reason))
                    {
                        notIdleReasons[checkResult.reason] = 0;
                    }

                    notIdleReasons[checkResult.reason]++;
                    if (hero.Clan == playerClan)
                    {
                        CmeDiagnostics.DebugLog($"[PHASE 3] [NOT_IDLE] {hero.Name}: {checkResult.reason}");
                    }

                    continue;
                }

                idleCount++;
                idleHeroesList.Add(hero);
                CmeDiagnostics.DebugLog($"[PHASE 3] [IDLE_FOUND] {hero.Name} in {hero.CurrentSettlement?.Name}");

                if (!HasAvailableClanPartySlot(playerClan))
                {
                    CmeDiagnostics.DebugLog($"[PHASE 4] Party slots filled. Found {idleCount} idle members, created {createdCount} parties. Stopping iteration.");
                    break;
                }

                if (TryCreatePartyForHero(hero))
                {
                    createdCount++;
                    CmeDiagnostics.DebugLog($"[PHASE 4] [SUCCESS] Created party for {hero.Name}");
                }
                else
                {
                    CmeDiagnostics.DebugLog($"[PHASE 4] [FAILED] Failed to create party for {hero.Name}");

                    if (settings.AutoJoinPlayerPartyWhenCreateFails && TryJoinHeroToPlayerParty(hero))
                    {
                        joinedPlayerPartyCount++;
                        CmeDiagnostics.DebugLog($"[PHASE 4] [FALLBACK_SUCCESS] Moved {hero.Name} to player party");
                    }
                }
            }

            CmeDiagnostics.DebugLog("=== DETAILED SUMMARY ===");
            CmeDiagnostics.DebugLog($"[SUMMARY] Total heroes checked: {Hero.AllAliveHeroes.Count()}");
            CmeDiagnostics.DebugLog($"[SUMMARY] Clan members (alive): {allClanHeroes.Count}");
            CmeDiagnostics.DebugLog($"[SUMMARY] Idle clan members found: {idleCount}");
            CmeDiagnostics.DebugLog($"[SUMMARY] Non-idle heroes checked: {notIdleCount}");
            CmeDiagnostics.DebugLog($"[SUMMARY] Parties created: {createdCount}");
            CmeDiagnostics.DebugLog($"[SUMMARY] Fallback joins to player party: {joinedPlayerPartyCount}");

            foreach (var kvp in notIdleReasons.OrderByDescending(x => x.Value))
            {
                CmeDiagnostics.DebugLog($"[BREAKDOWN] {kvp.Key}: {kvp.Value} heroes");
            }

            if (idleHeroesList.Count > 0)
            {
                CmeDiagnostics.DebugLog($"[IDLE_LIST] Idle members: {string.Join(", ", idleHeroesList.Select(h => h.Name.ToString()))}");
            }

            CmeDiagnostics.DebugLog("=== AutoCreatePartyForIdleClanMembers END ===");

            if (!settings.ShowNotifications)
            {
                return;
            }

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

            if (joinedPlayerPartyCount > 0)
            {
                var joinedText = new TextObject("{=CME_AUTO_JOIN_PLAYER_PARTY}Automatically moved {COUNT} idle clan member(s) to player party after party creation failed.");
                joinedText.SetTextVariable("COUNT", joinedPlayerPartyCount);
                InformationManager.DisplayMessage(new InformationMessage(joinedText.ToString()));
            }
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

            if (!hero.CanLeadParty())
            {
                return (false, "cannot lead party");
            }

            var settlement = hero.CurrentSettlement;
            if (settlement == null)
            {
                return (false, "current settlement is null");
            }

            if (!settlement.IsTown)
            {
                return (false, $"settlement is not a town: {settlement.Name}");
            }

            if (TryGetBoolProperty(hero, "StayingInSettlement", out var stayingInSettlement) && !stayingInSettlement)
            {
                return (false, "StayingInSettlement=false");
            }

            return (true, $"IDLE in town {settlement.Name}");
        }

        private static bool HasAvailableClanPartySlot(Clan playerClan)
        {
            if (!TryGetClanPartyLimit(playerClan, out var partyLimit))
            {
                CmeDiagnostics.DebugLog($"Could not determine party limit for clan {playerClan.Name}, assuming unlimited");
                return true;
            }

            var currentPartyCount = GetCurrentClanPartyCount(playerClan);

            CmeDiagnostics.DebugLog($"Party slot check for {playerClan.Name}: {currentPartyCount}/{partyLimit} slots used (main party included)");
            return currentPartyCount < partyLimit;
        }

        private static int GetCurrentClanPartyCount(Clan playerClan)
        {
            return MobileParty.All.Count(p =>
                p != null &&
                !p.IsDisbanding &&
                p.LeaderHero != null &&
                p.LeaderHero.Clan == playerClan);
        }

        private static bool TryGetClanPartyLimit(Clan playerClan, out int partyLimit)
        {
            partyLimit = 0;

            var campaign = Campaign.Current;
            if (campaign?.Models?.ClanTierModel == null)
            {
                return false;
            }

            var clanTierModel = campaign.Models.ClanTierModel;
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
                if (TryCreateLordPartyForHero(hero, out var createdLordParty))
                {
                    return createdLordParty;
                }

                var method = ClanPartyActionResolver.GetOrResolveCreatePartyMethod();
                if (method == null)
                {
                    CmeDiagnostics.DebugLog($"No create party method found for hero {hero.Name}");
                    return false;
                }

                var args = ClanPartyActionResolver.BuildArgumentsForCreatePartyMethod(method, hero);
                if (args == null)
                {
                    CmeDiagnostics.DebugLog($"Failed to build arguments for create party method for hero {hero.Name}");
                    return false;
                }

                var existingParty = hero.PartyBelongedTo;
                var result = method.Invoke(null, args);
                if (result is bool created)
                {
                    return created && HasCreatedPartyForHero(hero, existingParty);
                }

                return HasCreatedPartyForHero(hero, existingParty);
            }
            catch (Exception ex)
            {
                CmeDiagnostics.DebugLog($"[ERROR] Failed to create clan party for {hero.Name}: {ex}");
                return false;
            }
        }

        private static bool TryCreateLordPartyForHero(Hero hero, out bool created)
        {
            created = false;
            if (CreateLordPartyMethod == null)
            {
                return false;
            }

            var settlement = hero.CurrentSettlement;
            if (settlement == null)
            {
                CmeDiagnostics.DebugLog($"CreateLordParty prerequisites missing for {hero.Name}: settlement=false");
                return true;
            }

            try
            {
                var existingParty = hero.PartyBelongedTo;
                var partyId = $"cme_clan_party_{Guid.NewGuid():N}";
                var result = CreateLordPartyMethod.Invoke(null, new object?[]
                {
                    partyId,
                    hero,
                    settlement.GatePosition,
                    0f,
                    settlement,
                    hero
                });

                created = result is MobileParty party
                    ? party.LeaderHero == hero || HasCreatedPartyForHero(hero, existingParty)
                    : HasCreatedPartyForHero(hero, existingParty);

                return true;
            }
            catch (Exception ex)
            {
                CmeDiagnostics.DebugLog($"CreateLordParty failed for {hero.Name}: {ex.Message}");
                return false;
            }
        }

        private static bool HasCreatedPartyForHero(Hero hero, MobileParty? previousParty)
        {
            var currentParty = hero.PartyBelongedTo;
            if (currentParty != null && currentParty != previousParty)
            {
                return true;
            }

            return MobileParty.All.Any(p => p != null && !p.IsDisbanding && p.LeaderHero == hero && p != previousParty);
        }

        private static bool TryJoinHeroToPlayerParty(Hero hero)
        {
            try
            {
                var mainParty = MobileParty.MainParty;
                if (mainParty == null)
                {
                    CmeDiagnostics.DebugLog($"Player main party is null, cannot move {hero.Name}");
                    return false;
                }

                if (hero.PartyBelongedTo == mainParty)
                {
                    return true;
                }

                var method = ClanPartyActionResolver.GetOrResolveAddHeroToPartyMethod();
                if (method == null)
                {
                    CmeDiagnostics.DebugLog($"No add-hero-to-party method found for fallback join: {hero.Name}");
                    return false;
                }

                var args = ClanPartyActionResolver.BuildArgumentsForAddHeroToPartyMethod(method, hero, mainParty);
                if (args == null)
                {
                    CmeDiagnostics.DebugLog($"Failed to build add-hero arguments for {hero.Name}");
                    return false;
                }

                method.Invoke(null, args);
                return hero.PartyBelongedTo == mainParty;
            }
            catch (Exception ex)
            {
                CmeDiagnostics.DebugLog($"[ERROR] Failed fallback join to player party for {hero.Name}: {ex}");
                return false;
            }
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
    }
}
