using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    internal static class CmeDiagnostics
    {
        private const double DebugMessageDisplayCooldownSeconds = 5.0;

        private static bool? _debugLoggingEnabled;
        private static DateTime _lastSettingsCheckUtc = DateTime.MinValue;
        private static DateTime _lastMessageDisplayUtc = DateTime.MinValue;

        internal static void DebugLog(string message)
        {
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
                DebugNotify(message);
                Debug.Print($"[ClanManagerEnhanced.Debug] {message}");
            }
        }

        internal static void DebugNotify(string message, bool isImportant = false)
        {
            try
            {
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
                // Ignore notification failures.
            }
        }
    }
}
