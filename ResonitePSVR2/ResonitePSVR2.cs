using System;

using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

namespace ResonitePSVR2;

public partial class ResonitePSVR2 : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.1";
	public override string Name => "ResonitePSVR2";
	public override string Author => "tabithamoon";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/tabithamoon/ResonitePSVR2/";
	public static ModConfiguration? Config;
	
	public override void OnEngineInit() {
		Harmony harmony = new Harmony("com.tabbynet.ResonitePSVR2");
		Config = GetConfiguration();
		Config?.Save();	
		Msg("Loaded ResonitePSVR2");
		harmony.PatchAll();
		Engine engine = Engine.Current;
		engine.RunPostInit(() => {
			try {
				engine.InputInterface.RegisterInputDriver(new EyeTrackingDriver());
			} catch (Exception ex) {
				Msg($"Failed to initialize ResonitePSVR2! Exception: {ex}");
			}
		});
	}
}
