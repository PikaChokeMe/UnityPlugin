using Leap;
using Leap.Unity;
using Leap.Unity.Encoding;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Bone = Leap.Bone;
using Hand = Leap.Hand;

namespace Ultraleap.Tracking.OpenXR
{
    public class OpenXRLeapProvider : LeapProvider
    {
        private Frame _updateFrame = new Frame();
        private Hand _leftHand = new Hand();
        private Hand _rightHand = new Hand();

        [Tooltip("Specifies the main camera. Falls back to Camera.main if not set")]
        [SerializeField]
        private Camera _mainCamera; // Redundant backing field, used to present value in editor at parent level
        /// <summary>
        /// Specifies the main camera.
        /// Falls back to Camera.main if not set.
        /// </summary>
        public Camera mainCamera
        {
            get
            {
                if (_mainCamera != null)
                {
                    if (_mainCamera != MainCameraProvider.mainCamera)
                    {
                        MainCameraProvider.mainCamera = _mainCamera;
                    }
                }

                return _mainCamera;
            }
            set
            {
                _mainCamera = value;
                MainCameraProvider.mainCamera = value;
            }
        }

        protected void OnEnable()
        {
            // Assign the main camera if it looks like one is available and it's not yet been set on the backing field
            // NB this may be the case if the provider is created via AddComponent, as in MRTK
            if (mainCamera == null && MainCameraProvider.mainCamera != null)
            {
                mainCamera = MainCameraProvider.mainCamera;
            }

#if XR_LEGACY_INPUT_AVAILABLE
            if (mainCamera.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>() == null)
            {
                mainCamera.gameObject.AddComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>().UseRelativeTransform = true;
            }
#endif
        }

        private void Update()
        {
            PopulateLeapFrame(FrameTime.OnUpdate, ref _updateFrame);
            DispatchUpdateFrameEvent(_updateFrame);
        }

        void FixedUpdate()
        {
            DispatchFixedFrameEvent(_updateFrame);
        }

        private void PopulateLeapFrame(FrameTime frameTime, ref Frame leapFrame)
        {
            leapFrame.Hands.Clear();
            if (PopulateLeapHandFromOpenXRJoints(HandTracker.Left, frameTime, ref _leftHand))
            {
                leapFrame.Hands.Add(_leftHand);
            }

            if (PopulateLeapHandFromOpenXRJoints(HandTracker.Right, frameTime, ref _rightHand))
            {
                leapFrame.Hands.Add(_rightHand);
            }
        }

        private bool PopulateLeapHandFromOpenXRJoints(HandTracker handTracker, FrameTime frameTime, ref Hand hand)
        {
            var joints = new HandJointLocation[handTracker.JointCount];
            if (!handTracker.TryLocateHandJoints(frameTime, joints))
            {
                return false;
            }

            var parentTransform = mainCamera.transform.parent;
            var parentPosition = Vector3.zero;
            var parentRotation = Quaternion.identity;
            
            if (mainCamera.transform.parent != null)
            {
                parentPosition = parentTransform.position;
                parentRotation = parentTransform.rotation;
            }

            for (int fingerIndex = 0; fingerIndex < 5; fingerIndex++)
            {
                var xrTipIndex = 0;
                var fingerWidth = 0f;
                var fingerLength = 0f;

                for (int boneIndex = 0; boneIndex < 4; boneIndex++)
                {
                    var xrPrevIndex = fingerIndex * 5 + boneIndex + 1;
                    var xrNextIndex = xrPrevIndex + 1;
                    var prevJoint = joints[xrPrevIndex];
                    var nextJoint = joints[xrNextIndex];

                    // Ignore thumb Metacarpal
                    if (fingerIndex == 0 && boneIndex == 0)
                    {
                        var metacarpalPosition = (joints[(int)HandJoint.ThumbMetacarpal].Pose.position + parentPosition).ToVector();
                        var metacarpalRotation = joints[(int)HandJoint.ThumbMetacarpal].Pose.rotation * parentRotation;
                        hand.GetBone(boneIndex).Fill(
                            metacarpalPosition,
                            metacarpalPosition,
                            metacarpalPosition,
                            (metacarpalRotation * Vector3.forward).ToVector(),
                            0f,
                            joints[(int)HandJoint.ThumbMetacarpal].Radius * 2f,
                            (Bone.BoneType)boneIndex,
                            (metacarpalRotation * parentRotation).ToLeapQuaternion());
                        continue;
                    }

                    // Populate the finger bone information
                    var bone = hand.Fingers[fingerIndex].bones[boneIndex];
                    var prevJointPosition = (prevJoint.Pose.position + parentPosition).ToVector();
                    var prevJointRotation = (prevJoint.Pose.rotation * parentRotation);
                    var nextJointPosition = (nextJoint.Pose.position + parentPosition).ToVector();
                    bone.Fill(
                        prevJointPosition,
                        nextJointPosition,
                        (prevJointPosition + nextJointPosition) / 2f,
                        (prevJointRotation * Vector3.forward).ToVector(),
                        (prevJointPosition - nextJointPosition).Magnitude,
                        prevJoint.Radius * 2f,
                        (Bone.BoneType)boneIndex,
                        prevJointRotation.ToLeapQuaternion());
                    fingerWidth = Math.Max(fingerWidth, bone.Width);
                    fingerLength += bone.Length;
                    xrTipIndex = xrNextIndex;
                }

                // Populate the higher - level finger data.
                hand.Fingers[fingerIndex].Fill(
                    -1,
                    (handTracker == HandTracker.Left ? 0 : 1),
                    fingerIndex,
                    10f, // Fixed for now
                    (joints[xrTipIndex].Pose.position + parentPosition).ToVector(),
                    (joints[xrTipIndex].Pose.rotation * parentRotation * Vector3.forward).ToVector(),
                    fingerWidth,
                    fingerLength,
                    true, // Fixed for now
                    (Finger.FingerType)fingerIndex);
            }

            var palmWidth = joints[(int)HandJoint.Palm].Radius * 2.0f;
            var palmPosition = (joints[(int)HandJoint.Palm].Pose.position + parentPosition).ToVector();
            var palmRotation = joints[(int)HandJoint.Palm].Pose.rotation * parentRotation;
            var wristPosition = (joints[(int)HandJoint.Wrist].Pose.position + parentPosition).ToVector();
            var wristWidth = joints[(int)HandJoint.Wrist].Radius * 2f;

            // Populate the whole hand information.
            hand.Fill(
                -1,
                (handTracker == HandTracker.Left ? 0 : 1),
                1f,
                0.5f, // Fixed for now
                100f, // Fixed for now
                CalculatePinchStrength(ref hand, palmWidth),
                CalculatePinchDistance(ref hand),
                palmWidth,
                handTracker == HandTracker.Left,
                10f, // Fixed for now
                null, // Already Populated
                palmPosition,
                palmPosition,
                joints[(int)HandJoint.Palm].LinearVelocity.ToVector(),
                (palmRotation * Vector3.down).ToVector(),
                palmRotation.ToLeapQuaternion(),
                (palmRotation * Vector3.forward).ToVector(),
                wristPosition
            );

            // Fill arm data.
            if (handTracker.JointSet == HandJointSet.HandWithForearm)
            {
                var elbowPosition = (joints[(int)HandJoint.Elbow].Pose.position + parentPosition).ToVector();
                var elbowRotation = joints[(int)HandJoint.Elbow].Pose.rotation * parentRotation;
                var elbowDirection = elbowRotation * parentRotation * Vector3.back;
                var elbowLength = (elbowPosition - palmPosition).Magnitude;
                var centerPosition = (elbowPosition + palmPosition) / 2f;
                hand.Arm.Fill(
                    elbowPosition,
                    wristPosition,
                    centerPosition,
                    elbowDirection.ToVector(),
                    elbowLength,
                    wristWidth,
                    elbowRotation.ToLeapQuaternion()
                );
            }
            else
            {
                var elbowRotation = joints[(int)HandJoint.Palm].Pose.rotation * parentRotation;
                var elbowDirection = (elbowRotation * parentRotation * Vector3.back).ToVector();
                var elbowPosition = palmPosition + (elbowDirection * 0.3f);
                var elbowLength = 0.3f;
                var centerPosition = (elbowPosition + palmPosition) / 2f;
                hand.Arm.Fill(
                    elbowPosition,
                    wristPosition,
                    centerPosition,
                    elbowDirection,
                    elbowLength,
                    wristWidth,
                    elbowRotation.ToLeapQuaternion()
                );
            }
            return true;
        }

        private float CalculatePinchStrength(ref Hand hand, float palmWidth)
        {
            // Magic values taken from existing LeapC implementation (scaled to metres)
            float handScale = palmWidth / 0.08425f;
            float distanceZero = 0.0600f * handScale;
            float distanceOne = 0.0220f * handScale;

            // Get the thumb position.
            var thumbTipPosition = hand.GetThumb().TipPosition;

            // Compute the distance midpoints between the thumb and the each finger and find the smallest.
            var minDistanceSquared = float.MaxValue;
            foreach (var finger in hand.Fingers.Skip(1))
            {
                var distanceSquared = (finger.TipPosition - thumbTipPosition).MagnitudeSquared;
                minDistanceSquared = Mathf.Min(distanceSquared, minDistanceSquared);
            }

            // Compute the pinch strength.
            return Mathf.Clamp01((Mathf.Sqrt(minDistanceSquared) - distanceZero) / (distanceOne - distanceZero));
        }

        private float CalculateBoneDistanceSquared(Bone boneA, Bone boneB)
        {
            // Denormalize directions to bone length.
            var boneAJoint = boneA.PrevJoint;
            var boneBJoint = boneB.PrevJoint;
            var boneADirection = boneA.Direction * boneA.Length;
            var boneBDirection = boneB.Direction * boneB.Length;

            // Compute the minimum (squared) distance between two bones.
            var diff = boneBJoint - boneAJoint;
            var d1 = boneADirection.Dot(diff);
            var d2 = boneBDirection.Dot(diff);
            var a = boneADirection.MagnitudeSquared;
            var b = boneADirection.Dot(boneBDirection);
            var c = boneBDirection.MagnitudeSquared;
            var det = b * b - a * c;
            var t1 = Mathf.Clamp01((b * d2 - c * d1) / det);
            var t2 = Mathf.Clamp01((a * d2 - b * d1) / det);
            var pa = boneAJoint + t1 * boneADirection;
            var pb = boneBJoint + t2 * boneBDirection;
            return (pa - pb).MagnitudeSquared;
        }

        private float CalculatePinchDistance(ref Hand hand)
        {
            // Get the farthest 2 segments of thumb and index finger, respectively, and compute distances.
            var minDistanceSquared = float.MaxValue;
            foreach (var thumbBone in hand.GetThumb().bones.Skip(2))
            {
                foreach (var indexBone in hand.GetIndex().bones.Skip(2))
                {
                    var distanceSquared = CalculateBoneDistanceSquared(thumbBone, indexBone);
                    minDistanceSquared = Mathf.Min(distanceSquared, minDistanceSquared);
                }
            }

            // Return the pinch distance, converted to millimeters to match other providers.
            return Mathf.Sqrt(minDistanceSquared) * 1000.0f;
        }

        #region LeapProvider Implementation

        [JetBrains.Annotations.NotNull]
        public override Frame CurrentFrame
        {
            get
            {
#if UNITY_EDITOR
                return !Application.isPlaying ? EditTimeFrame : _updateFrame;
#else
                return _updateFrame;
#endif
            }
        }

        [JetBrains.Annotations.NotNull]
        public override Frame CurrentFixedFrame => CurrentFrame;

#if UNITY_EDITOR
        private Frame _backingUntransformedEditTimeFrame;
        private Frame _backingEditTimeFrame;

        private Frame EditTimeFrame
        {
            get
            {
                (_backingEditTimeFrame ??= new Frame()).Hands.Clear();
                (_backingUntransformedEditTimeFrame ??= new Frame()).Hands.Clear();
                _backingUntransformedEditTimeFrame.Hands.Add(EditTimeLeftHand);
                _backingUntransformedEditTimeFrame.Hands.Add(EditTimeRightHand);
                _backingEditTimeFrame.CopyFrom(_backingUntransformedEditTimeFrame).Transform(transform.GetLeapMatrix());
                return _backingEditTimeFrame;
            }
        }

        private readonly Dictionary<TestHandFactory.TestHandPose, Hand> _cachedLeftHands
            = new Dictionary<TestHandFactory.TestHandPose, Hand>();

        private Hand EditTimeLeftHand
        {
            get
            {
                if (_cachedLeftHands.TryGetValue(editTimePose, out Hand cachedHand))
                {
                    return cachedHand;
                }

                cachedHand = TestHandFactory.MakeTestHand(true, editTimePose);
                _cachedLeftHands[editTimePose] = cachedHand;
                return cachedHand;
            }
        }

        private readonly Dictionary<TestHandFactory.TestHandPose, Hand> _cachedRightHands
            = new Dictionary<TestHandFactory.TestHandPose, Hand>();

        private Hand EditTimeRightHand
        {
            get
            {
                if (_cachedRightHands.TryGetValue(editTimePose, out Hand cachedHand))
                {
                    return cachedHand;
                }

                cachedHand = TestHandFactory.MakeTestHand(false, editTimePose);
                _cachedRightHands[editTimePose] = cachedHand;
                return cachedHand;
            }
        }
#endif

        #endregion
    }
}