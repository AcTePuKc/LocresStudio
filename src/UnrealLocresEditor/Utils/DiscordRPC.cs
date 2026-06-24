using System;
using DiscordRPC;
using DiscordRPC.Logging;
using UnrealLocresEditor.Models;

namespace UnrealLocresEditor.Utils
{
    public class DiscordService
    {
        private DiscordRpcClient? _client;
        private const string ApplicationId = "1447101407701500017";

        public DiscordService()
        {
            if (PlatformUtils.IsLinux())
            {
                return;
            }

            var config = AppConfig.Instance;
            if (config.DiscordRPCEnabled)
            {
                Initialize();
            }
        }

        // CHANGE: "private" -> "public" so MainWindow can call it
        public void Initialize()
        {
            if (PlatformUtils.IsLinux())
            {
                return;
            }

            try
            {
                if (_client != null && _client.IsInitialized) return;

                _client = new DiscordRpcClient(ApplicationId)
                {
                    Logger = new ConsoleLogger { Level = LogLevel.Warning }
                };

                _client.Initialize();
                UpdatePresence(null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Discord RPC failed to initialize: {ex.Message}");
            }
        }

        public void UpdatePresence(LocresDocument? document)
        {
            if (PlatformUtils.IsLinux())
            {
                return;
            }

            var config = AppConfig.Instance;

            if (!config.DiscordRPCEnabled)
            {
                if (_client != null)
                {
                    _client.Dispose();
                    _client = null;
                }
                return;
            }

            if (_client == null || !_client.IsInitialized)
            {
                Initialize();
            }

            if (_client == null) return;

            bool isPrivate = config.DiscordRPCPrivacy;
            string details = "Idle";
            string state = "Main Menu";

            if (document != null)
            {
                if (isPrivate)
                {
                    details = config.DiscordRPCPrivacyString;
                    state = "Private Mode";
                }
                else
                {
                    details = $"Editing: {document.DisplayName}";
                    state = $"{document.Rows.Count} Strings";
                }
            }

            try
            {
                _client.SetPresence(new RichPresence
                {
                    Details = details,
                    State = state,
                    Assets = new Assets
                    {
                        LargeImageKey = "ule-icon",
                        LargeImageText = "UnrealLocresEditor",
                    },
                    Timestamps = Timestamps.Now
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating presence: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
