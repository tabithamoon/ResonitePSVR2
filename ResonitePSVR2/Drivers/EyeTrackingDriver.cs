using System;

using Elements.Core;
using FrooxEngine;
using ResonitePSVR2.ToolkitInterop;

namespace ResonitePSVR2;

public class EyeTrackingDriver : IInputDriver {
	private GazeVector3 _rightEyeLastValidGaze, _leftEyeLastValidGaze;
	private float _rightEyeLastValidDilation, _leftEyeLastValidDilation;

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
		ResonitePSVR2.Msg("Attempting to connect to PSVR2Toolkit");

		try {
			if (IpcClient.Instance().Start()) {
				ResonitePSVR2.Msg("Connected to PSVR2Toolkit");
				eyes = new(input, "PS VR2 Datastream", true);
				
				// Register to listen for events
				i.Engine.OnShutdown += Shutdown;
			} else {
				ResonitePSVR2.Msg("Failed to  connect to PSVR2Toolkit!");
			}
		} catch (Exception ex) {
			ResonitePSVR2.Msg($"Failed to connect to PSVR2Toolkit! Exception: {ex}");
		}
	}

	public void UpdateInputs(float deltaTime) {
		if (eyes != null && input != null) {
			bool eyeTrackingEnabled = ResonitePSVR2.EnableEyeTracking;

			eyes.IsDeviceActive = eyeTrackingEnabled;
			eyes.IsEyeTrackingActive = eyeTrackingEnabled;

			lock (_lock) {
				if (eyeTrackingEnabled) {
					UpdateEyes(eyes);
					eyes.ComputeCombinedEyeParameters();
					eyes.FinishUpdate();
				}
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
			_leftEyeLastValidGaze = leftEyeData.gazeDirNorm;
		} else {
			dest.LeftEye.UpdateWithRotation(floatQ.LookRotation(
				new float3(-_leftEyeLastValidGaze.x, _leftEyeLastValidGaze.y, _leftEyeLastValidGaze.z)
			));
		}
		
		if (leftEyeData.isPupilDiaValid) {
			dest.LeftEye.PupilDiameter = leftEyeData.pupilDiaMm / 1000; // Divide by 1000 to turn millimeter into meters
			_leftEyeLastValidDilation = leftEyeData.pupilDiaMm;
		} else {
			dest.LeftEye.PupilDiameter = _leftEyeLastValidDilation / 1000;
		}
		
		// right eye data
		if (rightEyeData.isGazeDirValid) {
			dest.RightEye.UpdateWithRotation(floatQ.LookRotation(
				new float3(-rightEyeData.gazeDirNorm.x, rightEyeData.gazeDirNorm.y, rightEyeData.gazeDirNorm.z)
			));
			_rightEyeLastValidGaze = rightEyeData.gazeDirNorm;
		} else {
			dest.RightEye.UpdateWithRotation(floatQ.LookRotation(
				new float3(-_rightEyeLastValidGaze.x, _rightEyeLastValidGaze.y, _rightEyeLastValidGaze.z)
			));
		}
		
		if (rightEyeData.isPupilDiaValid) {
			dest.RightEye.PupilDiameter = rightEyeData.pupilDiaMm / 1000;
			_rightEyeLastValidDilation = rightEyeData.pupilDiaMm;
		} else {
			dest.RightEye.PupilDiameter = _rightEyeLastValidDilation / 1000;
		}

		if (leftEyeData.isBlinkValid) {
			float leftOpenness = eyeTrackingData.leftEye.isOpenEnabled ? eyeTrackingData.leftEye.open : (eyeTrackingData.leftEye.blink ? 0 : 1);
			if (_leftEyeOpenLowPass != null) {
				leftOpenness = _leftEyeOpenLowPass.FilterValue(leftOpenness);
			}
			dest.LeftEye.Openness = leftOpenness;
		}
		if (rightEyeData.isBlinkValid) {
			float rightOpenness = eyeTrackingData.rightEye.isOpenEnabled ? eyeTrackingData.rightEye.open : (eyeTrackingData.rightEye.blink ? 0 : 1);
			if (_rightEyeOpenLowPass != null) {
				rightOpenness = _rightEyeOpenLowPass.FilterValue(rightOpenness);
			}
			dest.RightEye.Openness = rightOpenness;
		}
		
		dest.LeftEye.IsTracking = true;
		dest.RightEye.IsTracking = true;
		
		// unsupported data
		dest.LeftEye.Widen = 0f;
		dest.RightEye.Widen = 0f;
		dest.LeftEye.Squeeze = 0f;
		dest.RightEye.Squeeze = 0f;
		dest.LeftEye.InnerBrowVertical = 0f;
		dest.RightEye.InnerBrowVertical = 0f;
	}

	private void Shutdown() {
		// Tear down PSVR2Toolkit IPC client
		IpcClient.Instance().Stop();
	}
}
