using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    internal static class ClanPartyActionResolver
    {
        private static bool _createPartyMethodResolved;
        private static MethodInfo? _createPartyMethod;
        private static bool _addHeroToPartyMethodResolved;
        private static MethodInfo? _addHeroToPartyMethod;

        internal static MethodInfo? GetOrResolveCreatePartyMethod()
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

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
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

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_ERR_NO_CREATE_PARTY_METHOD}[ERROR] No compatible create-party action method found.").ToString()));
            Debug.Print("[ClanManagerEnhanced] No compatible create-party action method found.");
            return null;
        }

        internal static MethodInfo? GetOrResolveAddHeroToPartyMethod()
        {
            if (_addHeroToPartyMethodResolved)
            {
                return _addHeroToPartyMethod;
            }

            _addHeroToPartyMethodResolved = true;

            var campaignAssembly = typeof(Campaign).Assembly;
            var candidateTypeNames = new[]
            {
                "TaleWorlds.CampaignSystem.Actions.AddHeroToPartyAction",
                "TaleWorlds.CampaignSystem.Actions.ChangeHeroPartyAction",
                "TaleWorlds.CampaignSystem.Actions.HeroSpawnCampaignBehavior"
            };

            foreach (var typeName in candidateTypeNames)
            {
                var candidateType = campaignAssembly.GetType(typeName, false);
                var candidateMethod = FindUsableAddHeroToPartyApplyMethod(candidateType);
                if (candidateMethod != null)
                {
                    _addHeroToPartyMethod = candidateMethod;
                    return _addHeroToPartyMethod;
                }
            }

            foreach (var type in campaignAssembly.GetTypes())
            {
                if (type.Name.IndexOf("Hero", StringComparison.OrdinalIgnoreCase) < 0 ||
                    type.Name.IndexOf("Party", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var candidateMethod = FindUsableAddHeroToPartyApplyMethod(type);
                if (candidateMethod != null)
                {
                    _addHeroToPartyMethod = candidateMethod;
                    return _addHeroToPartyMethod;
                }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_ERR_NO_ADD_HERO_METHOD}[ERROR] No compatible add-hero-to-party action method found.").ToString()));
            Debug.Print("[ClanManagerEnhanced] No compatible add-hero-to-party action method found.");
            return null;
        }

        internal static object?[]? BuildArgumentsForCreatePartyMethod(MethodInfo method, Hero hero)
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

        internal static object?[]? BuildArgumentsForAddHeroToPartyMethod(MethodInfo method, Hero hero, MobileParty? targetParty)
        {
            if (targetParty == null)
            {
                return null;
            }

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

                if (paramType == typeof(MobileParty))
                {
                    args[i] = targetParty;
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

                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                    continue;
                }

                return null;
            }

            return args;
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

                if (BuildArgumentsForCreatePartyMethod(method, Hero.MainHero) != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo? FindUsableAddHeroToPartyApplyMethod(Type? candidateType)
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
                if (parameters.Length == 0 ||
                    !parameters.Any(p => p.ParameterType == typeof(Hero)) ||
                    !parameters.Any(p => p.ParameterType == typeof(MobileParty)))
                {
                    continue;
                }

                if (BuildArgumentsForAddHeroToPartyMethod(method, Hero.MainHero, MobileParty.MainParty) != null)
                {
                    return method;
                }
            }

            return null;
        }
    }
}
