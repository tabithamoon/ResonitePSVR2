using ResoniteModLoader;
using System.Reflection;

namespace ResonitePSVR2;

public partial class ResonitePSVR2 : ResoniteMod {
	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<bool> EnableEyeTracking_Config = new("eye_tracking", "Enable eye tracking", () => true );
	public static bool EnableEyeTracking => Config!.GetValue(EnableEyeTracking_Config);
	
	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<bool> EnableBlinkFiltering_Config = new("blink_flitering", "Enable blink smoothing", () => true );
	public static bool EnableBlinkFiltering => Config!.GetValue(EnableBlinkFiltering_Config);
	
	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<bool> EnableEyeLidEstimation_Config = new("eyelid_estimation", "Enable eye lid estimation", () => false );
	public static bool EnableEyeLidEstimation => Config!.GetValue(EnableEyeLidEstimation_Config);
	
	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<float> BlinkFilteringSpeed_Config = new("blink_filtering_speed", "Blink filtering speed", () => 20f);
	public static float BlinkFilteringSpeed => Config!.GetValue(BlinkFilteringSpeed_Config);
}
