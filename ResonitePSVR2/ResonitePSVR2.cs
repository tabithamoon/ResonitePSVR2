using System;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using ResonitePSVR2.PSVR2Toolkit;

namespace ResonitePSVR2;

public partial class ResonitePSVR2 : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.1.2";
	public override string Name => "ResonitePSVR2";
	public override string Author => "tabithamoon";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/tabithamoon/ResonitePSVR2/";
	public static ModConfiguration? Config;
	
	public override void OnEngineInit() {
		Harmony harmony = new Harmony("com.tabbynet.ResonitePSVR2");
		Config = GetConfiguration();
		Config?.Save();	

		harmony.PatchAll();
		Engine engine = Engine.Current;
		engine.RunPostInit(() => {
			Msg("Loaded ResonitePSVR2.");
			int initCode;

			// Blow up if we can't load psvr2_toolkit_capi_loader
			try {
				initCode = PSVR2ToolkitCAPI.Init();
			} catch (DllNotFoundException ex) {
				Msg($"{ex.Message}");
				return;
			}
			
			// Bail if we can't talk to PSVR2Toolkit (SteamVR not running?)
			if (initCode != 0) {
				Msg("Failed to connect to PSVR2Tookit. Is SteamVR running, and is PSVR2Toolkit installed?");
				return;
			}

			// Stop IPC client on engine shutdown
			engine.OnShutdown += () => {
				try {
					PSVR2ToolkitCAPI.Deinit();
				} catch (Exception ex) {
					Msg($"Error during PSVR2 Toolkit CAPI Teardown: {ex.Message}");
				}
			};
			
			try {
				if (EnableEyeTracking) engine.InputInterface.RegisterInputDriver(new EyeTrackingDriver());
			} catch (Exception ex) {
				Msg($"Failed to initialize eye tracking! Exception message: {ex.Message}");
			}
		});
	}
}
