using System;

using Elements.Core;
using FrooxEngine;
using ResonitePSVR2.ToolkitInterop;

namespace ResonitePSVR2;

public class EyeTrackingDriver : IInputDriver {
	// Blink smoothing lerp vars
	// There's probably a better way to do this...
	private float _leftIntermediate, _rightIntermediate, _leftOpen, _rightOpen, _leftOpenTarget, _rightOpenTarget;
	private bool _lerpInitialized;
	
	private CommandDataServerGazeDataResult2 _gazeData;
	public int UpdateOrder => 100;
	private Eyes? _eyes;
	
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
		if (ResonitePSVR2.EnableEyeLidEstimation && leftEye.isOpenEnabled && rightEye.isOpenEnabled) {
			_leftOpenTarget = leftEye.open;
			_rightOpenTarget = rightEye.open;
		} else {
			if (leftEye.isBlinkValid) _leftOpenTarget = leftEye.blink ? 0f : 1f;
			if (rightEye.isBlinkValid) _rightOpenTarget = rightEye.blink ? 0f : 1f;
		}
		ResonitePSVR2.Msg("Past target set");
		ResonitePSVR2.Msg($"leftOpenTarget: {_leftOpenTarget}, rightOpenTarget: {_rightOpenTarget}");
		ResonitePSVR2.Msg($"leftOpen: {_leftOpen}, rightOpen: {_rightOpen}");

		if (ResonitePSVR2.EnableBlinkFiltering) {
			if (!_lerpInitialized) {
				_leftIntermediate = _leftOpenTarget;
				_rightIntermediate = _rightOpenTarget;
				_lerpInitialized = true;
			}
			
			_leftOpen = MathX.SmoothLerp(
				_leftOpen,
				_leftOpenTarget,
				ref _leftIntermediate,
				Userspace.Current.Time.Delta * ResonitePSVR2.BlinkFilteringSpeed
			);

			_rightOpen = MathX.SmoothLerp(
				_rightOpen,
				_rightOpenTarget,
				ref _rightIntermediate,
				Userspace.Current.Time.Delta * ResonitePSVR2.BlinkFilteringSpeed
			);
		} else {
			_lerpInitialized = false;
			_leftOpen = _leftOpenTarget;
			_rightOpen = _rightOpenTarget;
		}

		eyes.LeftEye.Openness = _leftOpen;
		eyes.RightEye.Openness = _rightOpen;
		eyes.CombinedEye.Openness = MathX.Average(_leftOpen, _rightOpen);
	}

	private float3 GetGazeDirection(GazeEyeResult2 trackingData) {
		return new float3(
			-trackingData.gazeDirNorm.x,
			trackingData.gazeDirNorm.y,
			trackingData.gazeDirNorm.z
		);
	}
}
