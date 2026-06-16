using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    internal sealed class ClanLogisticsService
    {
        internal void ReinforceLowStrengthParties(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_PLAYER_CLAN_NULL_REINFORCE}Player clan is null, skipping reinforcement.").ToString()));
                return;
            }

            var lowStrengthParties = MobileParty.All
                .Where(p => ClanPartyFilters.ShouldCheckParty(p, playerClan) && IsPartyBelowStrengthThreshold(p, settings))
                .ToList();

            var lowStrengthText = new TextObject("{=CME_DBG_REINFORCE_LOW_STRENGTH_FOUND}Found {LOW_COUNT} low-strength parties out of {TOTAL_COUNT} clan parties");
            lowStrengthText.SetTextVariable("LOW_COUNT", lowStrengthParties.Count);
            lowStrengthText.SetTextVariable("TOTAL_COUNT", MobileParty.All.Count(p => ClanPartyFilters.ShouldCheckParty(p, playerClan)));
            InformationManager.DisplayMessage(new InformationMessage(lowStrengthText.ToString()));

            if (lowStrengthParties.Count == 0)
            {
                return;
            }

            var castles = Settlement.All
                .Where(s => !s.IsVillage && !s.IsHideout && s.OwnerClan == playerClan && s.Town != null)
                .ToList();

            var overgarrisonedCastles = castles
                .Where(s => IsCastleOvergarrisoned(s, settings))
                .ToList();

            if (overgarrisonedCastles.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_REINFORCE_NO_OVERGARRISON}No overgarrisoned castles available for troop extraction").ToString()));
                return;
            }

            var totalReinforced = 0;
            foreach (var party in lowStrengthParties)
            {
                if (!IsPartyBelowStrengthThreshold(party, settings))
                {
                    continue;
                }

                var reinforcedCount = ExtractAndReinforceParty(party, overgarrisonedCastles, settings);
                totalReinforced += reinforcedCount;
                var reinforcedPartyText = new TextObject("{=CME_DBG_REINFORCE_PARTY_RESULT}Reinforced party {PARTY} with {COUNT} troops");
                reinforcedPartyText.SetTextVariable("PARTY", party.Name.ToString());
                reinforcedPartyText.SetTextVariable("COUNT", reinforcedCount);
                InformationManager.DisplayMessage(new InformationMessage(reinforcedPartyText.ToString()));
            }

            if (totalReinforced > 0 && settings.ShowNotifications)
            {
                var text = new TextObject("{=CME_REINFORCE_SUCCESS}Reinforced party troops with {COUNT} soldiers from castles.");
                text.SetTextVariable("COUNT", totalReinforced);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
        }

        internal void TransferExcessPrisonersTodungeons(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_PLAYER_CLAN_NULL_PRISONER_TRANSFER}Player clan is null, skipping prisoner transfer.").ToString()));
                return;
            }

            var overloadedParties = MobileParty.All
                .Where(p => ClanPartyFilters.ShouldCheckParty(p, playerClan) && IsPartyOverloadedWithPrisoners(p, settings))
                .ToList();

            if (overloadedParties.Count == 0)
            {
                return;
            }

            var castles = Settlement.All
                .Where(s => !s.IsVillage && !s.IsHideout && s.OwnerClan == playerClan && s.Town != null)
                .ToList();

            if (castles.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_PRISONER_TRANSFER_NO_CASTLE}No castles available for prisoner transfer").ToString()));
                return;
            }

            var totalTransferred = 0;
            foreach (var party in overloadedParties)
            {
                if (!IsPartyOverloadedWithPrisoners(party, settings))
                {
                    continue;
                }

                var transferredCount = TransferPrisonersToClosestCastle(party, castles);
                totalTransferred += transferredCount;
                var transferredPartyText = new TextObject("{=CME_DBG_PRISONER_TRANSFER_PARTY_RESULT}Transferred {COUNT} prisoners from party {PARTY}");
                transferredPartyText.SetTextVariable("COUNT", transferredCount);
                transferredPartyText.SetTextVariable("PARTY", party.Name.ToString());
                InformationManager.DisplayMessage(new InformationMessage(transferredPartyText.ToString()));
            }

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

                var totalExtracted = 0;
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

                    var remainingNeeded = amountToAdd - totalExtracted;
                    var extracted = ExtractTroopsFromGarrison(garrisonParty, targetParty, remainingNeeded);
                    totalExtracted += extracted;
                }

                return totalExtracted;
            }
            catch (Exception ex)
            {
                var reinforceErrorText = new TextObject("{=CME_ERR_REINFORCE_FAILED}[ERROR] Failed to reinforce party: {DETAIL}");
                reinforceErrorText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(reinforceErrorText.ToString()));
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

                var extracted = 0;
                var troopsByTier = new SortedDictionary<int, List<CharacterObject>>();

                for (var i = garrison.Count - 1; i >= 0; i--)
                {
                    var character = garrison.GetCharacterAtIndex(i);
                    if (character == null)
                    {
                        continue;
                    }

                    var tier = character.Tier;
                    if (!troopsByTier.ContainsKey(-tier))
                    {
                        troopsByTier[-tier] = new List<CharacterObject>();
                    }

                    var count = garrison.GetElementNumber(i);
                    for (var j = 0; j < count && extracted < maxExtract; j++)
                    {
                        troopsByTier[-tier].Add(character);
                        extracted++;
                    }
                }

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
                var extractErrorText = new TextObject("{=CME_ERR_EXTRACT_TROOPS_FAILED}[ERROR] Failed to extract troops from garrison: {DETAIL}");
                extractErrorText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(extractErrorText.ToString()));
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

                var bestCastle = castles.FirstOrDefault();
                if (bestCastle?.Town?.GarrisonParty == null)
                {
                    return 0;
                }

                var transferred = 0;
                var prisonRoster = party.PrisonRoster;
                var garrisonParty = bestCastle.Town.GarrisonParty;
                var dungeonRoster = garrisonParty.PrisonRoster;

                if (dungeonRoster == null)
                {
                    return 0;
                }

                var dungeonCapacity = garrisonParty.Party?.PartySizeLimit ?? 300;
                var currentDungeonCount = dungeonRoster.TotalHealthyCount;
                var availableSpace = dungeonCapacity - currentDungeonCount;

                if (availableSpace <= 0)
                {
                    return 0;
                }

                for (var i = prisonRoster.Count - 1; i >= 0 && transferred < availableSpace; i--)
                {
                    var character = prisonRoster.GetCharacterAtIndex(i);
                    if (character == null)
                    {
                        continue;
                    }

                    var count = prisonRoster.GetElementNumber(i);
                    var toTransfer = Math.Min(count, availableSpace - transferred);
                    prisonRoster.AddToCounts(character, -toTransfer);
                    dungeonRoster.AddToCounts(character, toTransfer);
                    transferred += toTransfer;
                }

                return transferred;
            }
            catch (Exception ex)
            {
                var transferErrorText = new TextObject("{=CME_ERR_TRANSFER_PRISONERS_FAILED}[ERROR] Failed to transfer prisoners: {DETAIL}");
                transferErrorText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(transferErrorText.ToString()));
                return 0;
            }
        }
    }
}
