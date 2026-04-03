using System;

using Elements.Core;
using FrooxEngine;
using ResonitePSVR2.ToolkitInterop;

namespace ResonitePSVR2;

public class EyeTrackingDriver : IInputDriver {
	private const int _noiseFilterSamples = 15;
	private LowPassFilter _rightEyeOpenLowPass = new(_noiseFilterSamples);
	private LowPassFilter _leftEyeOpenLowPass = new(_noiseFilterSamples);

	private readonly object _lock = new();
	private InputInterface? input;
	private Eyes? eyes;
	
	public int UpdateOrder => 150; // not even cyro knows what this does... I'm but a mere mortal
	
	public void CollectDeviceInfos(DataTreeList list) {
		ResonitePSVR2.Msg("Collecting PSVR2 device info");

		DataTreeDictionary eyeDict = new();
		eyeDict.Add("Name", "PS VR2 Eye Datastream");
		eyeDict.Add("Type", "Eye Tracking");
		eyeDict.Add("Model", "PlayStation VR2");
		
		list.Add(eyeDict);
	}

	public void RegisterInputs(InputInterface i) {
		input = i;
		eyes = new(input, "PlayStation VR2", true);
	}

	public void UpdateInputs(float deltaTime) {
		if (eyes != null && input != null) {
			eyes.IsDeviceActive = true;
			eyes.IsEyeTrackingActive = true;

			lock (_lock) {
				UpdateEyes(eyes);
				eyes.ComputeCombinedEyeParameters();
				eyes.FinishUpdate();
			}
		}
	}

	private void UpdateEyes(Eyes dest) {
		var eyeTrackingData = IpcClient.Instance().RequestEyeTrackingData();
		var leftEyeData = eyeTrackingData.leftEye;
		var rightEyeData = eyeTrackingData.rightEye;
		
		// left eye data
		if (leftEyeData.isGazeDirValid) {
			dest.LeftEye.UpdateWithRotation(floatQ.LookRotation(
				new float3(-leftEyeData.gazeDirNorm.x, leftEyeData.gazeDirNorm.y, leftEyeData.gazeDirNorm.z)
			));
		}
		
		if (leftEyeData.isPupilDiaValid) {
			dest.LeftEye.PupilDiameter = leftEyeData.pupilDiaMm / 1000; // Divide by 1000 to turn millimeter into meters
		}
		
		// right eye data
		if (rightEyeData.isGazeDirValid) {
			dest.RightEye.UpdateWithRotation(floatQ.LookRotation(
				new float3(-rightEyeData.gazeDirNorm.x, rightEyeData.gazeDirNorm.y, rightEyeData.gazeDirNorm.z)
			));
		}
		
		if (rightEyeData.isPupilDiaValid) {
			dest.RightEye.PupilDiameter = rightEyeData.pupilDiaMm / 1000;
		}

		// Ideally I'd replace the smoothing with the game's built in lerping solutions, instead of grabbing the one from the VRCFT module.
		// Alas, this is a fixup until the full PSVR2TK release, where we'll get proper expressions ;P
		if (leftEyeData.isBlinkValid) {
			float leftOpenness;
			if (ResonitePSVR2.EnableEyeLidEstimation && eyeTrackingData.leftEye.isOpenEnabled) leftOpenness = eyeTrackingData.leftEye.open;
			else leftOpenness = eyeTrackingData.leftEye.blink ? 0 : 1;
			
			if (ResonitePSVR2.EnableBlinkFiltering) leftOpenness = _leftEyeOpenLowPass.FilterValue(leftOpenness);
			dest.LeftEye.Openness = leftOpenness;
		}
		
		if (rightEyeData.isBlinkValid) {
			float rightOpenness;
			if (ResonitePSVR2.EnableEyeLidEstimation && eyeTrackingData.rightEye.isOpenEnabled) rightOpenness = eyeTrackingData.rightEye.open;
			else rightOpenness = eyeTrackingData.rightEye.blink ? 0 : 1;
			
			if (ResonitePSVR2.EnableBlinkFiltering) rightOpenness = _rightEyeOpenLowPass.FilterValue(rightOpenness);
			dest.RightEye.Openness = rightOpenness;
		}
		
		dest.LeftEye.IsTracking = true;
		dest.RightEye.IsTracking = true;
	}
}
