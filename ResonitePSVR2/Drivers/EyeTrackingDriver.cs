using System;

using Elements.Core;
using FrooxEngine;
using ResonitePSVR2.PSVR2Toolkit;

namespace ResonitePSVR2;

public class EyeTrackingDriver : IInputDriver {
	// Blink smoothing lerp vars
	private float _leftIntermediate, _rightIntermediate, _leftOpen, _rightOpen, _leftOpenTarget, _rightOpenTarget;
	private bool _lerpInitialized;
	
	private static float3 _invalidGazeDir = new (0f); // Invalid float3 for gaze direction check
	private hmd2_gaze_status_t _gazeStatus;
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
		
		// Don't update the eyes if we can't get gaze data
		if (!PSVR2ToolkitCAPI.GetGazeStatus(ref _gazeStatus, 50)) return;

		// Updates left, right and combined
		UpdateEyes(_eyes);
		
		_eyes.ComputeCombinedEyeParameters();
		_eyes.ConvergenceDistance = 0f;
		_eyes.Timestamp += deltaTime;
		_eyes.FinishUpdate();
	}

	// Bulk of the work here
	private void UpdateEyes(Eyes eyes) {
		eyes.LeftEye.IsDeviceActive = true;
		eyes.RightEye.IsDeviceActive = true;
		eyes.CombinedEye.IsDeviceActive = true;

		// Get gaze data
		hmd2_gaze_wearable_data_t gazeData = _gazeStatus.wearable;
		
		// Gazes
		if (_gazeStatus.wearable.left.is_gaze_origin_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE) {
			float3 gazeDir = GetGazeDirection(gazeData.left.gaze_dir_norm);
			if (gazeDir != _invalidGazeDir) {
				eyes.LeftEye.IsTracking = true;
				eyes.LeftEye.UpdateWithDirection(gazeDir);
			} else {
				eyes.LeftEye.IsTracking = false;
			}
		}

		if (_gazeStatus.wearable.right.is_gaze_origin_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE) {
			float3 gazeDir = GetGazeDirection(gazeData.right.gaze_dir_norm);
			if (gazeDir != _invalidGazeDir) {
				eyes.RightEye.IsTracking = true;
				eyes.RightEye.UpdateWithDirection(gazeDir);
			} else {
				eyes.RightEye.IsTracking = false;
			}
		}

		if (_gazeStatus.wearable.is_gaze_origin_combined_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE) {
			eyes.CombinedEye.IsTracking = true;
			eyes.CombinedEye.UpdateWithDirection(GetGazeDirection(gazeData.gaze_dir_combined_norm));
		}
		
		// Pupil dilation
		if (gazeData.left.is_pupil_dia_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE)
			eyes.LeftEye.PupilDiameter = gazeData.left.pupil_dia_mm / 1000;
		
		if (gazeData.right.is_pupil_dia_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE)
			eyes.RightEye.PupilDiameter = gazeData.right.pupil_dia_mm / 1000;

		if ((gazeData.left.is_pupil_dia_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE) && (gazeData.right.is_pupil_dia_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE))
			eyes.CombinedEye.PupilDiameter = MathX.Average(gazeData.left.pupil_dia_mm, gazeData.right.pupil_dia_mm) / 1000;
		
		// Blink
		if (gazeData.left.is_blink_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE)
			_leftOpenTarget = gazeData.left.blink == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE ? 0f : 1f;
		
		if (gazeData.right.is_blink_valid == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE)
			_rightOpenTarget = gazeData.right.blink == hmd2_gaze_bool_t.HMD2_GAZE_BOOL_TRUE ? 0f : 1f;

		// Check if userspace exists before getting deltas from it. Oops!
		if (ResonitePSVR2.EnableBlinkFiltering && Userspace.Current != null) {
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

	private float3 GetGazeDirection(hmd2_gaze_vec3_t eyeGazeVec3) {
		return new float3(
			-eyeGazeVec3.x,
			eyeGazeVec3.y,
			eyeGazeVec3.z
		);
	}
}
