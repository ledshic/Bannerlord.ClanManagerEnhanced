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
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_PLAYER_CLAN_NULL_AUTO_CREATE}Player clan is null, skipping auto party creation.").ToString()));
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_AUTO_CREATE_START}=== Auto Create Party For Idle Clan Members START ===").ToString()));

            var phase1ClanText = new TextObject("{=CME_DBG_PHASE1_CLAN}[PHASE 1] Player Clan: {CLAN}, Tier: {TIER}");
            phase1ClanText.SetTextVariable("CLAN", playerClan.Name.ToString());
            phase1ClanText.SetTextVariable("TIER", playerClan.Tier);
            InformationManager.DisplayMessage(new InformationMessage(phase1ClanText.ToString()));

            var hasAvailableSlot = HasAvailableClanPartySlot(playerClan);
            if (!hasAvailableSlot)
            {
                var currentPartyCount = GetCurrentClanPartyCount(playerClan);
                var noSlotText = new TextObject("{=CME_DBG_PHASE1_NO_SLOT}[PHASE 1] No available party slots. Current parties (including main): {COUNT}");
                noSlotText.SetTextVariable("COUNT", currentPartyCount);
                InformationManager.DisplayMessage(new InformationMessage(noSlotText.ToString()));
            }

            var allClanHeroes = Hero.AllAliveHeroes.Where(h => h.Clan == playerClan).ToList();
            var phase2CountText = new TextObject("{=CME_DBG_PHASE2_TOTAL_MEMBERS}[PHASE 2] Total clan members (alive): {COUNT}");
            phase2CountText.SetTextVariable("COUNT", allClanHeroes.Count);
            InformationManager.DisplayMessage(new InformationMessage(phase2CountText.ToString()));

            var idleCount = 0;
            var createdCount = 0;
            var joinedPlayerPartyCount = 0;
            var fallbackAttemptCount = 0;
            var fallbackFailedCount = 0;
            var notIdleCount = 0;
            var idleHeroesList = new List<Hero>();
            var notIdleReasons = new Dictionary<string, int>();

            foreach (var hero in Hero.AllAliveHeroes)
            {
                var checkResult = EvaluateIdleClanMemberWithDetails(hero, playerClan);
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
                        var notIdleText = new TextObject("{=CME_DBG_PHASE3_NOT_IDLE}[PHASE 3] [NOT_IDLE] {HERO}: {REASON}");
                        notIdleText.SetTextVariable("HERO", hero.Name.ToString());
                        notIdleText.SetTextVariable("REASON", GetIdleReasonText(checkResult.reason).ToString());
                        InformationManager.DisplayMessage(new InformationMessage(notIdleText.ToString()));
                    }

                    continue;
                }

                idleCount++;
                idleHeroesList.Add(hero);
                var idleFoundText = new TextObject("{=CME_DBG_PHASE3_IDLE_FOUND}[PHASE 3] [IDLE_FOUND] {HERO} in {SETTLEMENT}");
                idleFoundText.SetTextVariable("HERO", hero.Name.ToString());
                idleFoundText.SetTextVariable("SETTLEMENT", hero.CurrentSettlement?.Name?.ToString() ?? "-");
                InformationManager.DisplayMessage(new InformationMessage(idleFoundText.ToString()));

                if (!checkResult.canCreateParty)
                {
                    var createSkippedText = new TextObject("{=CME_DBG_PHASE4_CREATE_SKIPPED_REASON}[PHASE 4] [CREATE_SKIPPED] {HERO}: {REASON}");
                    createSkippedText.SetTextVariable("HERO", hero.Name.ToString());
                    createSkippedText.SetTextVariable("REASON", GetIdleReasonText(checkResult.reason).ToString());
                    InformationManager.DisplayMessage(new InformationMessage(createSkippedText.ToString()));

                    if (settings.AutoJoinPlayerPartyWhenCreateFails)
                    {
                        fallbackAttemptCount++;
                        if (TryJoinHeroToPlayerParty(hero))
                        {
                            joinedPlayerPartyCount++;
                            var fallbackSuccessText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_SUCCESS}[PHASE 4] [FALLBACK_SUCCESS] Moved {HERO} to player party");
                            fallbackSuccessText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackSuccessText.ToString()));
                        }
                        else
                        {
                            fallbackFailedCount++;
                            var fallbackFailedText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_FAILED}[PHASE 4] [FALLBACK_FAILED] Failed to move {HERO} to player party");
                            fallbackFailedText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackFailedText.ToString()));
                        }
                    }

                    continue;
                }

                if (!hasAvailableSlot)
                {
                    var createSkippedNoSlotText = new TextObject("{=CME_DBG_PHASE4_CREATE_SKIPPED_NO_SLOT}[PHASE 4] [CREATE_SKIPPED] No available party slot for {HERO}");
                    createSkippedNoSlotText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(createSkippedNoSlotText.ToString()));

                    if (settings.AutoJoinPlayerPartyWhenCreateFails)
                    {
                        fallbackAttemptCount++;
                        if (TryJoinHeroToPlayerParty(hero))
                        {
                            joinedPlayerPartyCount++;
                            var fallbackSuccessText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_SUCCESS}[PHASE 4] [FALLBACK_SUCCESS] Moved {HERO} to player party");
                            fallbackSuccessText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackSuccessText.ToString()));
                        }
                        else
                        {
                            fallbackFailedCount++;
                            var fallbackFailedText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_FAILED}[PHASE 4] [FALLBACK_FAILED] Failed to move {HERO} to player party");
                            fallbackFailedText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackFailedText.ToString()));
                        }
                    }

                    continue;
                }

                if (TryCreatePartyForHero(hero))
                {
                    createdCount++;
                    var createSuccessText = new TextObject("{=CME_DBG_PHASE4_CREATE_SUCCESS}[PHASE 4] [SUCCESS] Created party for {HERO}");
                    createSuccessText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(createSuccessText.ToString()));
                }
                else
                {
                    var createFailedText = new TextObject("{=CME_DBG_PHASE4_CREATE_FAILED}[PHASE 4] [FAILED] Failed to create party for {HERO}");
                    createFailedText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(createFailedText.ToString()));

                    if (settings.AutoJoinPlayerPartyWhenCreateFails)
                    {
                        fallbackAttemptCount++;
                        if (TryJoinHeroToPlayerParty(hero))
                        {
                            joinedPlayerPartyCount++;
                            var fallbackSuccessText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_SUCCESS}[PHASE 4] [FALLBACK_SUCCESS] Moved {HERO} to player party");
                            fallbackSuccessText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackSuccessText.ToString()));
                        }
                        else
                        {
                            fallbackFailedCount++;
                            var fallbackFailedText = new TextObject("{=CME_DBG_PHASE4_FALLBACK_FAILED}[PHASE 4] [FALLBACK_FAILED] Failed to move {HERO} to player party");
                            fallbackFailedText.SetTextVariable("HERO", hero.Name.ToString());
                            InformationManager.DisplayMessage(new InformationMessage(fallbackFailedText.ToString()));
                        }
                    }
                }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_SUMMARY_HEADER}=== DETAILED SUMMARY ===").ToString()));

            var summaryTotalText = new TextObject("{=CME_DBG_SUMMARY_TOTAL_HEROES}[SUMMARY] Total heroes checked: {COUNT}");
            summaryTotalText.SetTextVariable("COUNT", Hero.AllAliveHeroes.Count());
            InformationManager.DisplayMessage(new InformationMessage(summaryTotalText.ToString()));

            var summaryClanText = new TextObject("{=CME_DBG_SUMMARY_CLAN_MEMBERS}[SUMMARY] Clan members (alive): {COUNT}");
            summaryClanText.SetTextVariable("COUNT", allClanHeroes.Count);
            InformationManager.DisplayMessage(new InformationMessage(summaryClanText.ToString()));

            var summaryIdleText = new TextObject("{=CME_DBG_SUMMARY_IDLE_FOUND}[SUMMARY] Idle clan members found: {COUNT}");
            summaryIdleText.SetTextVariable("COUNT", idleCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryIdleText.ToString()));

            var summaryNonIdleText = new TextObject("{=CME_DBG_SUMMARY_NON_IDLE}[SUMMARY] Non-idle heroes checked: {COUNT}");
            summaryNonIdleText.SetTextVariable("COUNT", notIdleCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryNonIdleText.ToString()));

            var summaryCreatedText = new TextObject("{=CME_DBG_SUMMARY_CREATED}[SUMMARY] Parties created: {COUNT}");
            summaryCreatedText.SetTextVariable("COUNT", createdCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryCreatedText.ToString()));

            var summaryFallbackText = new TextObject("{=CME_DBG_SUMMARY_FALLBACK}[SUMMARY] Fallback joins to player party: {COUNT}");
            summaryFallbackText.SetTextVariable("COUNT", joinedPlayerPartyCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryFallbackText.ToString()));

            var summaryFallbackAttemptText = new TextObject("{=CME_DBG_SUMMARY_FALLBACK_ATTEMPT}[SUMMARY] Fallback join attempts: {COUNT}");
            summaryFallbackAttemptText.SetTextVariable("COUNT", fallbackAttemptCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryFallbackAttemptText.ToString()));

            var summaryFallbackFailedText = new TextObject("{=CME_DBG_SUMMARY_FALLBACK_FAILED}[SUMMARY] Fallback join failed: {COUNT}");
            summaryFallbackFailedText.SetTextVariable("COUNT", fallbackFailedCount);
            InformationManager.DisplayMessage(new InformationMessage(summaryFallbackFailedText.ToString()));

            foreach (var kvp in notIdleReasons.OrderByDescending(x => x.Value))
            {
                var breakdownText = new TextObject("{=CME_DBG_SUMMARY_BREAKDOWN}[BREAKDOWN] {REASON}: {COUNT} heroes");
                breakdownText.SetTextVariable("REASON", GetIdleReasonText(kvp.Key).ToString());
                breakdownText.SetTextVariable("COUNT", kvp.Value);
                InformationManager.DisplayMessage(new InformationMessage(breakdownText.ToString()));
            }

            if (idleHeroesList.Count > 0)
            {
                var idleListText = new TextObject("{=CME_DBG_SUMMARY_IDLE_LIST}[IDLE_LIST] Idle members: {LIST}");
                idleListText.SetTextVariable("LIST", string.Join(", ", idleHeroesList.Select(h => h.Name.ToString())));
                InformationManager.DisplayMessage(new InformationMessage(idleListText.ToString()));
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_AUTO_CREATE_END}=== Auto Create Party For Idle Clan Members END ===").ToString()));

            if (!settings.ShowNotifications)
            {
                return;
            }

            if (idleCount > 0)
            {
                var idleText = new TextObject("{=CME_IDLE_MEMBERS_FOUND}Found {COUNT} idle clan member(s) in settlements.");
                idleText.SetTextVariable("COUNT", idleCount);
                InformationManager.DisplayMessage(new InformationMessage(idleText.ToString()));
            }

            if (createdCount > 0)
            {
                var createdText = new TextObject("{=CME_AUTO_CREATE_PARTY_CREATED}Automatically created {COUNT} clan party(ies) from idle settlement members.");
                createdText.SetTextVariable("COUNT", createdCount);
                InformationManager.DisplayMessage(new InformationMessage(createdText.ToString()));
            }

            if (joinedPlayerPartyCount > 0)
            {
                var joinedText = new TextObject("{=CME_AUTO_JOIN_PLAYER_PARTY}Automatically moved {COUNT} idle clan member(s) to player party after party creation failed.");
                joinedText.SetTextVariable("COUNT", joinedPlayerPartyCount);
                InformationManager.DisplayMessage(new InformationMessage(joinedText.ToString()));
            }

            if (settings.AutoJoinPlayerPartyWhenCreateFails)
            {
                var fallbackResultText = new TextObject("{=CME_AUTO_RETURN_RESULT}Auto-return check completed. Attempted: {ATTEMPT}, succeeded: {SUCCESS}, failed: {FAILED}.");
                fallbackResultText.SetTextVariable("ATTEMPT", fallbackAttemptCount);
                fallbackResultText.SetTextVariable("SUCCESS", joinedPlayerPartyCount);
                fallbackResultText.SetTextVariable("FAILED", fallbackFailedCount);
                InformationManager.DisplayMessage(new InformationMessage(fallbackResultText.ToString()));
            }
        }

        private static (bool isIdle, bool canCreateParty, string reason) EvaluateIdleClanMemberWithDetails(Hero? hero, Clan playerClan)
        {
            if (hero == null)
            {
                return (false, false, "CME_REASON_HERO_NULL");
            }

            if (hero == Hero.MainHero)
            {
                return (false, false, "CME_REASON_IS_MAIN_HERO");
            }

            if (hero.Clan != playerClan)
            {
                return (false, false, "CME_REASON_CLAN_MISMATCH");
            }

            if (hero.IsDead)
            {
                return (false, false, "CME_REASON_HERO_DEAD");
            }

            if (hero.IsChild)
            {
                return (false, false, "CME_REASON_HERO_CHILD");
            }

            if (hero.IsPrisoner)
            {
                return (false, false, "CME_REASON_HERO_PRISONER");
            }

            if (hero.PartyBelongedTo != null)
            {
                return (false, false, "CME_REASON_ALREADY_IN_PARTY");
            }

            if (hero.GovernorOf != null)
            {
                return (false, false, "CME_REASON_IS_GOVERNOR");
            }

            var settlement = hero.CurrentSettlement;
            if (settlement == null)
            {
                return (false, false, "CME_REASON_NO_SETTLEMENT");
            }

            if (!settlement.IsTown && !settlement.IsCastle)
            {
                return (false, false, "CME_REASON_NOT_TOWN_CASTLE");
            }

            if (TryGetBoolProperty(hero, "StayingInSettlement", out var stayingInSettlement) && !stayingInSettlement)
            {
                return (false, false, "CME_REASON_NOT_STAYING_IN_SETTLEMENT");
            }

            if (!hero.CanLeadParty())
            {
                return (true, false, "CME_REASON_CANNOT_LEAD_PARTY");
            }

            return (true, true, "CME_REASON_IDLE_SETTLEMENT");
        }

        private static TextObject GetIdleReasonText(string reasonId)
        {
            switch (reasonId)
            {
                case "CME_REASON_HERO_NULL":
                    return new TextObject("{=CME_REASON_HERO_NULL}hero is null");
                case "CME_REASON_IS_MAIN_HERO":
                    return new TextObject("{=CME_REASON_IS_MAIN_HERO}is main hero");
                case "CME_REASON_CLAN_MISMATCH":
                    return new TextObject("{=CME_REASON_CLAN_MISMATCH}clan mismatch");
                case "CME_REASON_HERO_DEAD":
                    return new TextObject("{=CME_REASON_HERO_DEAD}hero is dead");
                case "CME_REASON_HERO_CHILD":
                    return new TextObject("{=CME_REASON_HERO_CHILD}hero is a child");
                case "CME_REASON_HERO_PRISONER":
                    return new TextObject("{=CME_REASON_HERO_PRISONER}hero is a prisoner");
                case "CME_REASON_ALREADY_IN_PARTY":
                    return new TextObject("{=CME_REASON_ALREADY_IN_PARTY}already in party");
                case "CME_REASON_IS_GOVERNOR":
                    return new TextObject("{=CME_REASON_IS_GOVERNOR}is governor");
                case "CME_REASON_NO_SETTLEMENT":
                    return new TextObject("{=CME_REASON_NO_SETTLEMENT}current settlement is null");
                case "CME_REASON_NOT_TOWN_CASTLE":
                    return new TextObject("{=CME_REASON_NOT_TOWN_CASTLE}settlement is not a town/castle");
                case "CME_REASON_NOT_STAYING_IN_SETTLEMENT":
                    return new TextObject("{=CME_REASON_NOT_STAYING_IN_SETTLEMENT}staying in settlement is false");
                case "CME_REASON_CANNOT_LEAD_PARTY":
                    return new TextObject("{=CME_REASON_CANNOT_LEAD_PARTY}cannot lead party");
                case "CME_REASON_IDLE_SETTLEMENT":
                    return new TextObject("{=CME_REASON_IDLE_SETTLEMENT}idle in settlement");
                default:
                    return new TextObject(reasonId);
            }
        }

        private static bool HasAvailableClanPartySlot(Clan playerClan)
        {
            if (!TryGetClanPartyLimit(playerClan, out var partyLimit))
            {
                var limitUnknownText = new TextObject("{=CME_DBG_PARTY_LIMIT_UNKNOWN}Could not determine party limit for clan {CLAN}, assuming unlimited");
                limitUnknownText.SetTextVariable("CLAN", playerClan.Name.ToString());
                InformationManager.DisplayMessage(new InformationMessage(limitUnknownText.ToString()));
                return true;
            }

            var currentPartyCount = GetCurrentClanPartyCount(playerClan);

            var slotCheckText = new TextObject("{=CME_DBG_PARTY_SLOT_CHECK}Party slot check for {CLAN}: {CURRENT}/{LIMIT} slots used (main party included)");
            slotCheckText.SetTextVariable("CLAN", playerClan.Name.ToString());
            slotCheckText.SetTextVariable("CURRENT", currentPartyCount);
            slotCheckText.SetTextVariable("LIMIT", partyLimit);
            InformationManager.DisplayMessage(new InformationMessage(slotCheckText.ToString()));
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
                    var noCreateMethodText = new TextObject("{=CME_DBG_NO_CREATE_METHOD}No create party method found for hero {HERO}");
                    noCreateMethodText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(noCreateMethodText.ToString()));
                    return false;
                }

                var args = ClanPartyActionResolver.BuildArgumentsForCreatePartyMethod(method, hero);
                if (args == null)
                {
                    var buildCreateArgsFailedText = new TextObject("{=CME_DBG_CREATE_ARGS_BUILD_FAILED}Failed to build arguments for create party method for hero {HERO}");
                    buildCreateArgsFailedText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(buildCreateArgsFailedText.ToString()));
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
                var createExceptionText = new TextObject("{=CME_ERR_CREATE_PARTY_EXCEPTION}[ERROR] Failed to create clan party for {HERO}: {DETAIL}");
                createExceptionText.SetTextVariable("HERO", hero.Name.ToString());
                createExceptionText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(createExceptionText.ToString()));
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
                var lordPrereqText = new TextObject("{=CME_DBG_CREATE_LORD_PREREQ_MISSING}CreateLordParty prerequisites missing for {HERO}: settlement=false");
                lordPrereqText.SetTextVariable("HERO", hero.Name.ToString());
                InformationManager.DisplayMessage(new InformationMessage(lordPrereqText.ToString()));
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
                var lordCreateFailedText = new TextObject("{=CME_ERR_CREATE_LORD_FAILED}CreateLordParty failed for {HERO}: {DETAIL}");
                lordCreateFailedText.SetTextVariable("HERO", hero.Name.ToString());
                lordCreateFailedText.SetTextVariable("DETAIL", ex.Message);
                InformationManager.DisplayMessage(new InformationMessage(lordCreateFailedText.ToString()));
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
                    var mainPartyNullText = new TextObject("{=CME_DBG_MAIN_PARTY_NULL}Player main party is null, cannot move {HERO}");
                    mainPartyNullText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(mainPartyNullText.ToString()));
                    return false;
                }

                if (hero.PartyBelongedTo == mainParty)
                {
                    return true;
                }

                var method = ClanPartyActionResolver.GetOrResolveAddHeroToPartyMethod();
                if (method == null)
                {
                    var noAddMethodText = new TextObject("{=CME_DBG_NO_ADD_HERO_METHOD}No add-hero-to-party method found for fallback join: {HERO}");
                    noAddMethodText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(noAddMethodText.ToString()));
                    return false;
                }

                var args = ClanPartyActionResolver.BuildArgumentsForAddHeroToPartyMethod(method, hero, mainParty);
                if (args == null)
                {
                    var buildAddArgsFailedText = new TextObject("{=CME_DBG_ADD_HERO_ARGS_BUILD_FAILED}Failed to build add-hero arguments for {HERO}");
                    buildAddArgsFailedText.SetTextVariable("HERO", hero.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(buildAddArgsFailedText.ToString()));
                    return false;
                }

                method.Invoke(null, args);
                return hero.PartyBelongedTo == mainParty;
            }
            catch (Exception ex)
            {
                var fallbackExceptionText = new TextObject("{=CME_ERR_FALLBACK_JOIN_EXCEPTION}[ERROR] Failed fallback join to player party for {HERO}: {DETAIL}");
                fallbackExceptionText.SetTextVariable("HERO", hero.Name.ToString());
                fallbackExceptionText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(fallbackExceptionText.ToString()));
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
