using System;

using Elements.Core;
using FrooxEngine;
using ResonitePSVR2.ToolkitInterop;

namespace ResonitePSVR2;

public class EyeTrackingDriver : IInputDriver {
	private const int _noiseFilterSamples = 15;
	private LowPassFilter _rightEyeOpenLowPass = new(_noiseFilterSamples);
	private LowPassFilter _leftEyeOpenLowPass = new(_noiseFilterSamples);

	private CommandDataServerGazeDataResult2 _gazeData;
	private Eyes? _eyes;
	
	public int UpdateOrder => 100;
	
	public void CollectDeviceInfos(DataTreeList list) {
		DataTreeDictionary eyeDict = new();
		eyeDict.Add("Name", "PS VR2 Eye Tracking Data");
		eyeDict.Add("Type", "Eye Tracking");
		eyeDict.Add("Model", "PlayStation VR2");
		list.Add(eyeDict);
		
		ResonitePSVR2.Msg("Eye tracking data stream registered.");
	}

	public void RegisterInputs(InputInterface input) {
		_eyes = new(input, "PlayStation VR2", true);
	}

	public void UpdateInputs(float deltaTime) {
		if (_eyes is null) return;
		if (!Engine.Current.InputInterface.VR_Active || !ResonitePSVR2.EnableEyeTracking) {
			_eyes.IsEyeTrackingActive = false;
			return;
		}

		_eyes.IsEyeTrackingActive = true;
		_gazeData = IpcClient.Instance().RequestEyeTrackingData();

		// Updates left, right and combined
		UpdateEyes(_eyes, _gazeData.leftEye, _gazeData.rightEye);
		
		_eyes.ComputeCombinedEyeParameters();
		_eyes.ConvergenceDistance = 0f;
		_eyes.Timestamp += deltaTime;
		_eyes.FinishUpdate();
	}

	// Bulk of the work here
	private void UpdateEyes(Eyes eyes, GazeEyeResult2 leftEye, GazeEyeResult2 rightEye) {
		eyes.LeftEye.IsDeviceActive = true;
		eyes.RightEye.IsDeviceActive = true;
		eyes.CombinedEye.IsDeviceActive = true;
		
		// Gazes
		if (leftEye.isGazeDirValid) {
			eyes.LeftEye.IsTracking = true;
			eyes.LeftEye.UpdateWithDirection(GetGazeDirection(leftEye));
		}

		if (rightEye.isGazeDirValid) {
			eyes.RightEye.IsTracking = true;
			eyes.RightEye.UpdateWithDirection(GetGazeDirection(rightEye));
		}

		if (leftEye.isGazeDirValid && rightEye.isGazeDirValid) {
			eyes.CombinedEye.IsTracking = true;
			eyes.CombinedEye.UpdateWithDirection(
				MathX.Average(GetGazeDirection(leftEye), GetGazeDirection(rightEye))
			);
		}
		
		// Pupil dilation
		if (leftEye.isPupilDiaValid)
			eyes.LeftEye.PupilDiameter = leftEye.pupilDiaMm / 1000;
		
		if (rightEye.isPupilDiaValid)
			eyes.RightEye.PupilDiameter = rightEye.pupilDiaMm / 1000;

		if (leftEye.isPupilDiaValid && rightEye.isPupilDiaValid)
			eyes.CombinedEye.PupilDiameter = MathX.Average(leftEye.pupilDiaMm, rightEye.pupilDiaMm) / 1000;
		
		// Openness
		// Ideally I'd replace the smoothing with the game's built in lerping solutions, instead of grabbing the one from the VRCFT module.
		// Alas, this is a fixup until the full PSVR2TK release, where we'll get proper expressions ;P
		// The smoothing is force enabled if using eyelid estimation.
		float leftOpenness = 0, rightOpenness = 0;
		if (leftEye.isBlinkValid) {
			if (ResonitePSVR2.EnableEyeLidEstimation && leftEye.isOpenEnabled)
				leftOpenness = leftEye.open;
			else
				leftOpenness = leftEye.blink ? 0 : 1;
			
			if (ResonitePSVR2.EnableBlinkFiltering || ResonitePSVR2.EnableEyeLidEstimation)
				leftOpenness = _leftEyeOpenLowPass.FilterValue(leftOpenness);
			
			eyes.LeftEye.Openness = leftOpenness;
		}

		if (rightEye.isBlinkValid) {
			if (ResonitePSVR2.EnableEyeLidEstimation && rightEye.isOpenEnabled)
				rightOpenness = rightEye.open;
			else
				rightOpenness = rightEye.blink ? 0 : 1;
			
			if (ResonitePSVR2.EnableBlinkFiltering)
				rightOpenness = _rightEyeOpenLowPass.FilterValue(rightOpenness);
			
			eyes.RightEye.Openness = rightOpenness;
		}

		if (leftEye.isBlinkValid && rightEye.isBlinkValid) {
			eyes.CombinedEye.Openness = MathX.Average(leftOpenness, rightOpenness);
		}
	}

	private float3 GetGazeDirection(GazeEyeResult2 trackingData) {
		return new float3(-trackingData.gazeDirNorm.x,
			trackingData.gazeDirNorm.y,
			trackingData.gazeDirNorm.z
		);
	}
}
