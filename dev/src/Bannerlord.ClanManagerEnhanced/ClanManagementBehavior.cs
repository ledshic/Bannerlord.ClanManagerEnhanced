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

            if (!settings.AllowPlayerClanPartiesJoinExternalArmies)
            {
                EnforceExternalArmyRestriction(settings);
            }

            if (settings.AutoCreatePartyForIdleClanMembers)
            {
                AutoCreatePartyForIdleClanMembers(settings);
            }

            if (settings.AutoReinforceParties)
            {
                ReinforceLowStrengthParties(settings);
            }

            if (settings.AutoTransferExcessPrisoners)
            {
                TransferExcessPrisonersTodungeons(settings);
            }

            if (settings.ShowNotifications)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DAILY_TICK}ClanManagerEnhanced daily check executed.").ToString()));
            }
        }

        private void AutoCreatePartyForIdleClanMembers(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                return;
            }

            if (!HasAvailableClanPartySlot(playerClan))
            {
                return;
            }

            int createdCount = 0;

            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (!IsIdleTavernClanMember(hero, playerClan))
                {
                    continue;
                }

                if (!HasAvailableClanPartySlot(playerClan))
                {
                    break;
                }

                if (TryCreatePartyForHero(hero))
                {
                    createdCount++;
                }
            }

            if (createdCount > 0 && settings.ShowNotifications)
            {
                var text = new TextObject("{=CME_AUTO_CREATE_PARTY_CREATED}Automatically created {COUNT} clan party(ies) from idle tavern members.");
                text.SetTextVariable("COUNT", createdCount);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
        }

        private void EnforceExternalArmyRestriction(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
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
                    continue;
                }

                if (TryForceLeaveArmy(party, army) && settings.ShowNotifications)
                {
                    blockedCount++;
                    blockedPartyName ??= party.Name?.ToString();
                }
            }

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

        private static bool IsIdleTavernClanMember(Hero? hero, Clan playerClan)
        {
            if (hero == null || hero == Hero.MainHero)
            {
                return false;
            }

            if (hero.Clan != playerClan || hero.IsDead || hero.IsChild || hero.IsPrisoner)
            {
                return false;
            }

            if (hero.PartyBelongedTo != null || hero.GovernorOf != null)
            {
                return false;
            }

            var settlement = hero.CurrentSettlement;
            if (settlement == null || !settlement.IsTown)
            {
                return false;
            }

            // For compatibility across game versions, we only enforce this when the property exists.
            if (TryGetBoolProperty(hero, "StayingInSettlement", out var stayingInSettlement) && !stayingInSettlement)
            {
                return false;
            }

            return true;
        }

        private static bool HasAvailableClanPartySlot(Clan playerClan)
        {
            if (!TryGetClanPartyLimit(playerClan, out var partyLimit))
            {
                return true;
            }

            var currentPartyCount = MobileParty.All.Count(p =>
                p != null &&
                !p.IsMainParty &&
                !p.IsDisbanding &&
                p.LeaderHero != null &&
                p.LeaderHero.Clan == playerClan);

            return currentPartyCount < partyLimit;
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
                    return false;
                }

                var args = BuildArgumentsForMethod(method, hero);
                if (args == null)
                {
                    return false;
                }

                var result = method.Invoke(null, args);
                return result is bool created ? created : true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[ClanManagerEnhanced] Failed to create clan party for {hero.Name}: {ex}");
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
                Debug.Print($"[ClanManagerEnhanced] Failed to remove party from army: {ex}");
            }

            return false;
        }

        private void ReinforceLowStrengthParties(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                return;
            }

            var lowStrengthParties = MobileParty.All
                .Where(p => ShouldCheckParty(p, playerClan) && IsPartyBelowStrengthThreshold(p, settings))
                .ToList();

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

            if (overgarrisonedCastles.Count == 0)
            {
                return;
            }

            int totalReinforced = 0;
            foreach (var party in lowStrengthParties)
            {
                if (IsPartyBelowStrengthThreshold(party, settings))
                {
                    int reinforcedCount = ExtractAndReinforceParty(party, overgarrisonedCastles, settings);
                    totalReinforced += reinforcedCount;
                }
            }

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
                return;
            }

            var overloadedParties = MobileParty.All
                .Where(p => ShouldCheckParty(p, playerClan) && IsPartyOverloadedWithPrisoners(p, settings))
                .ToList();

            if (overloadedParties.Count == 0)
            {
                return;
            }

            var castles = Settlement.All
                .Where(s => s.IsVillage == false && s.IsHideout == false && s.OwnerClan == playerClan && s.Town != null)
                .ToList();

            if (castles.Count == 0)
            {
                return;
            }

            int totalTransferred = 0;
            foreach (var party in overloadedParties)
            {
                if (IsPartyOverloadedWithPrisoners(party, settings))
                {
                    int transferredCount = TransferPrisonersToClosestCastle(party, castles);
                    totalTransferred += transferredCount;
                }
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
                Debug.Print($"[ClanManagerEnhanced] Failed to transfer prisoners: {ex}");
                return 0;
            }
        }
    }
}
