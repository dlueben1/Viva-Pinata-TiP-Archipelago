using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using VivaPinata.AP.Client.Configuration;
using VivaPinata.AP.Client.Template;
#if DEBUG
using System.Diagnostics;
#endif

namespace VivaPinata.AP.Client
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        private readonly RexGlueProbe _rexGlueProbe;

        private PlayerHookProbe? _playerHookProbe;
        private CoinAdditionProbe? _coinAdditionProbe;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            //#if DEBUG
            //            // Attaches debugger in debug mode; ignored in release.
            //            Debugger.Launch();
            //#endif

            // For more information about this template, please see
            // https://reloaded-project.github.io/Reloaded-II/ModTemplate/

            // If you want to implement e.g. unload support in your mod,
            // and some other neat features, override the methods in ModBase.

            // TODO: Implement some mod logic
            _logger.WriteLine(
                $"[VivaPinata.AP] Client initialized inside PID {Environment.ProcessId}!"
            );

            _logger.WriteLine($"[VivaPinata.AP] Process: {Environment.ProcessPath}");

            _logger.WriteLine($"[VivaPinata.AP] Native hooks available: {_hooks is not null}");
            _logger.WriteLine(
                $"[VivaPinata.AP] Runtime probes: player stats={_configuration.EnablePlayerStatsProbe}, "
                    + $"coin additions={_configuration.EnableCoinAdditionProbe}."
            );

            bool enablePlayerStatsProbe = _configuration.EnablePlayerStatsProbe;
            bool enableCoinAdditionProbe = _configuration.EnableCoinAdditionProbe;

            _rexGlueProbe = new RexGlueProbe(
                _logger,
                enablePlayerStatsProbe,
                enableCoinAdditionProbe,
                (playerNativeFunction, coinAdditionNativeFunction) =>
                {
                    if (_hooks is null)
                    {
                        _logger.WriteLine(
                            "[AP] Cannot install runtime probes: Reloaded.Hooks unavailable."
                        );
                        return;
                    }

                    if (enablePlayerStatsProbe)
                        _playerHookProbe = new PlayerHookProbe(
                            _logger,
                            _hooks,
                            playerNativeFunction
                        );

                    if (enableCoinAdditionProbe)
                        _coinAdditionProbe = new CoinAdditionProbe(
                            _logger,
                            _hooks,
                            coinAdditionNativeFunction
                        );
                }
            );
        }

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }

        public override void Disposing()
        {
            _rexGlueProbe?.Dispose();
            _playerHookProbe?.Dispose();
            _coinAdditionProbe?.Dispose();
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}
