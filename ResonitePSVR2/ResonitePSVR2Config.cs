using ResoniteModLoader;
using System.Reflection;

namespace ResonitePSVR2;

public partial class ResonitePSVR2 : ResoniteMod {
	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<bool> EnableEyeTracking_Config = new("eye_tracking", "Enable eye tracking", () => true );
	public static bool EnableEyeTracking => Config!.GetValue(EnableEyeTracking_Config);

	[AutoRegisterConfigKey]
	internal static ModConfigurationKey<bool> EnableSmoothBlinking_Config = new("smooth_blinking", "Smoothen out binary blinking");

	public static bool EnableSmoothBlinking => Config!.GetValue(EnableSmoothBlinking_Config);
}
