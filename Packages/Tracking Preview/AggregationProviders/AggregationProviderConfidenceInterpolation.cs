/******************************************************************************
 * Copyright (C) Ultraleap, Inc. 2011-2021.                                   *
 *                                                                            *
 * Use subject to the terms of the Apache License 2.0 available at            *
 * http://www.apache.org/licenses/LICENSE-2.0, or another agreement           *
 * between Ultraleap and you, your company or other organization.             *
 ******************************************************************************/

using Leap.Unity.Encoding;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Leap.Unity
{

    /// <summary>
    /// possible structure to implement our own aggregation code,
    /// calculates hand and joint confidences and interpolates linearly between hands based on their confidences,
    /// it uses hand confidences for the overall position and orientation of the hand (palmPos and PalmRot),
    /// and joint confidences for the per joint positions relative to the hand Pose
    /// </summary>
    public class AggregationProviderConfidenceInterpolation : LeapAggregatedProviderBase
    {
        // factors that get multiplied to the corresponding cofidence values to get an overall weighted confidence value

        public float palmPosFactor = 1;
        public float palmRotFactor = 1;
        public float palmVelocityFactor = 1;
        public float lengthVisibleFactor = 1;

        public float jointRotFactor = 1;
        public float jointRotToPalmFactor = 1;

        // if the debug hands are not null, their joint colors are given by interpolating between the debugColors based on joint confidences
        public CapsuleHand debugHandLeft;
        public CapsuleHand debugHandRight;

        // The debug colors should have the same order and length as the provider list
        public Color[] debugColors;

        Dictionary<LeapProvider, LastHandPositions> lastLeftHandPositions = new Dictionary<LeapProvider, LastHandPositions>();
        Dictionary<LeapProvider, LastHandPositions> lastRightHandPositions = new Dictionary<LeapProvider, LastHandPositions>();

        Dictionary<LeapProvider, float> leftHandFirstVisible = new Dictionary<LeapProvider, float>();
        Dictionary<LeapProvider, float> rightHandFirstVisible = new Dictionary<LeapProvider, float>();


        protected override Frame MergeFrames(Frame[] frames)
        {
            List<Hand> leftHands = new List<Hand>();
            List<Hand> rightHands = new List<Hand>();

            List<float> leftHandConfidences = new List<float>();
            List<float> rightHandConfidences = new List<float>();

            List<float[]> leftJointConfidences = new List<float[]>();
            List<float[]> rightJointConfidences = new List<float[]>();


            // make lists of all left and right hands found in each frame and also make a list of their confidences
            for (int i = 0; i < frames.Length; i++)
            {
                Frame frame = frames[i];
                AddFrameToLengthVisibleDicts(frames, i);

                foreach (Hand hand in frame.Hands)
                {
                    if (hand.IsLeft)
                    {
                        leftHands.Add(hand);

                        float handConfidence = CalculateHandConfidence(i, hand);
                        float[] jointConfidences = CalculateJointConfidence(i, hand);

                        leftHandConfidences.Add(handConfidence);
                        leftJointConfidences.Add(jointConfidences.Select(x => x * handConfidence).ToArray());
                    }

                    else
                    {
                        rightHands.Add(hand);

                        float handConfidence = CalculateHandConfidence(i, hand);
                        float[] jointConfidences = CalculateJointConfidence(i, hand);

                        rightHandConfidences.Add(handConfidence);
                        rightJointConfidences.Add(jointConfidences.Select(x => x * handConfidence).ToArray());
                    }
                }
            }


            // normalize hand confidences:
            float sum = leftHandConfidences.Sum();
            if (sum != 0)
            {
                for (int i = 0; i < leftHandConfidences.Count; i++)
                {
                    leftHandConfidences[i] /= sum;
                }
            }
            sum = rightHandConfidences.Sum();
            if (sum != 0)
            {
                for (int i = 0; i < rightHandConfidences.Count; i++)
                {
                    rightHandConfidences[i] /= sum;
                }
            }

            // normalize joint confidences:
            for (int i = 0; i < VectorHand.NUM_JOINT_POSITIONS; i++)
            {
                sum = leftJointConfidences.Sum(x => x[i]);
                if (sum != 0)
                {
                    for (int j = 0; j < leftJointConfidences.Count; j++)
                    {
                        leftJointConfidences[j][i] /= sum;
                    }
                }
                else
                {
                    for (int j = 0; j < leftJointConfidences.Count; j++)
                    {
                        leftJointConfidences[j][i] = 1f / leftJointConfidences.Count;
                    }
                }

                sum = rightJointConfidences.Sum(x => x[i]);
                if (sum != 0)
                {
                    for (int j = 0; j < rightJointConfidences.Count; j++)
                    {
                        rightJointConfidences[j][i] /= sum;
                    }
                }
                else
                {
                    for (int j = 0; j < rightJointConfidences.Count; j++)
                    {
                        rightJointConfidences[j][i] = 1f / rightJointConfidences.Count;
                    }
                }
            }



            // combine hands using their confidences
            List<Hand> mergedHands = new List<Hand>();

            if (leftHands.Count > 0)
            {
                mergedHands.Add(MergeHands(leftHands, leftHandConfidences, leftJointConfidences));
            }

            if (rightHands.Count > 0)
            {
                mergedHands.Add(MergeHands(rightHands, rightHandConfidences, rightJointConfidences));
            }

            // get frame data from first frame and add merged hands to it
            Frame mergedFrame = frames[0];
            mergedFrame.Hands = mergedHands;

            return mergedFrame;

        }

        /// <summary>
        /// Merge hands based on hand confidences and joint confidences
        /// </summary>
        public Hand MergeHands(List<Hand> hands, List<float> handConfidences, List<float[]> jointConfidences)
        {
            bool isLeft = hands[0].IsLeft;
            Vector3 mergedPalmPos = hands[0].PalmPosition.ToVector3() * handConfidences[0];
            Quaternion mergedPalmRot = hands[0].Rotation.ToQuaternion();

            for (int i = 1; i < hands.Count; i++)
            {
                // position
                mergedPalmPos += hands[i].PalmPosition.ToVector3() * handConfidences[i];

                // rotation
                float lerpValue = handConfidences.Take(i).Sum() / handConfidences.Take(i + 1).Sum();
                mergedPalmRot = Quaternion.Lerp(hands[i].Rotation.ToQuaternion(), mergedPalmRot, lerpValue);
            }

            // joints
            Vector3[] mergedJointPositions = new Vector3[VectorHand.NUM_JOINT_POSITIONS];
            List<VectorHand> vectorHands = new List<VectorHand>();
            foreach (Hand hand in hands)
            {
                vectorHands.Add(new VectorHand(hand));
            }

            for (int hands_idx = 0; hands_idx < hands.Count; hands_idx++)
            {
                for (int joint_idx = 0; joint_idx < VectorHand.NUM_JOINT_POSITIONS; joint_idx++)
                {
                    mergedJointPositions[joint_idx] += vectorHands[hands_idx].jointPositions[joint_idx] * jointConfidences[hands_idx][joint_idx];
                }
            }

            // combine everything to a hand
            Hand mergedHand = new Hand();
            new VectorHand(isLeft, mergedPalmPos, mergedPalmRot, mergedJointPositions).Decode(mergedHand);

            // visualize the joint merge:
            if (isLeft && debugHandLeft != null) VisualizeMergedJoints(debugHandLeft, jointConfidences);
            else if (!isLeft && debugHandRight != null) VisualizeMergedJoints(debugHandRight, jointConfidences);

            return mergedHand;
        }


        /// <summary>
        /// combine different confidence functions to get an overall confidence for the given hand
        /// uses frame_idx to find the corresponding provider that saw this hand
        /// </summary>
        public float CalculateHandConfidence(int frame_idx, Hand hand)
        {
            float confidence = 0;

            confidence = palmPosFactor * Confidence_RelativeHandPos(providers[frame_idx], providers[frame_idx].transform, hand.PalmPosition.ToVector3());
            confidence += palmRotFactor * Confidence_RelativeHandRot(providers[frame_idx].transform, hand.PalmPosition.ToVector3(), hand.PalmNormal.ToVector3());
            confidence += palmVelocityFactor * Confidence_RelativeHandVelocity(providers[frame_idx], providers[frame_idx].transform, hand.PalmPosition.ToVector3(), hand.IsLeft);
            confidence += lengthVisibleFactor * Confidence_LengthHandVisible(providers[frame_idx], hand.IsLeft);

            return confidence;
        }

        /// <summary>
        /// Combine different confidence functions to get an overall confidence for each joint in the given hand
        /// uses frame_idx to find the corresponding provider that saw this hand
        /// </summary>
        public float[] CalculateJointConfidence(int frame_idx, Hand hand)
        {
            float[] confidences = new float[VectorHand.NUM_JOINT_POSITIONS];

            float[] confidences_jointRot = Confidence_RelativeJointRot(providers[frame_idx].transform, hand);
            float[] confidences_jointPalmRot = Confidence_relativeJointRotToPalmRot(providers[frame_idx].transform, hand);

            for (int i = 0; i < confidences.Length; i++)
            {
                confidences[i] = jointRotFactor * confidences_jointRot[i] +
                                 jointRotToPalmFactor * confidences_jointPalmRot[i];
            }

            return confidences;
        }



        #region Hand Confidence Methods

        /// <summary>
        /// uses the hand pos relative to the device to calculate a confidence.
        /// using a 2d gauss with bigger spread when further away from the device
        /// and amplitude depending on the ideal depth of the specific device and 
        /// the distance from hand to device
        /// </summary>
        float Confidence_RelativeHandPos(LeapProvider provider, Transform deviceOrigin, Vector3 handPos)
        {
            Vector3 relativeHandPos = deviceOrigin.InverseTransformPoint(handPos);

            // 2d gauss

            // amplitude
            float a = 1 - relativeHandPos.y;
            // pos of center of peak
            float x0 = 0;
            float y0 = 0;
            // spread
            float sigmaX = relativeHandPos.y;
            float sigmaY = relativeHandPos.y;

            // input pos
            float x = relativeHandPos.x;
            float y = relativeHandPos.z;


            // if the frame is coming from a LeapServiceProvider, use different values depending on the type of device
            if (provider is LeapServiceProvider || provider.GetType().BaseType == typeof(LeapServiceProvider))
            {
                LeapServiceProvider serviceProvider = provider as LeapServiceProvider;
                Device.DeviceType deviceType = serviceProvider.CurrentDevice.Type;

                if (deviceType == Device.DeviceType.TYPE_RIGEL || deviceType == Device.DeviceType.TYPE_SIR170 || deviceType == Device.DeviceType.TYPE_3DI)
                {
                    // Depth: Between 10cm to 75cm preferred, up to 1m maximum
                    // Field Of View: 170 x 170 degrees typical (160 x 160 degrees minimum)
                    float currentDepth = relativeHandPos.y;

                    float requiredWidth = (currentDepth / 2) / Mathf.Sin(Mathf.Deg2Rad * 170 / 2);
                    sigmaX = 0.2f * requiredWidth;
                    sigmaY = 0.2f * requiredWidth;

                    // amplitude should be 1 within ideal depth and go 'smoothly' to zero on both sides of the ideal depth.
                    if (currentDepth > 0.1f && currentDepth < 0.75)
                    {
                        a = 1f;
                    }
                    else if (currentDepth < 0.1f)
                    {
                        a = 0.55f / (Mathf.PI / 2) * Mathf.Atan(100 * (currentDepth + 0.05f)) + 0.5f;
                    }
                    else if (currentDepth > 0.75f)
                    {
                        a = -0.55f / (Mathf.PI / 2) * Mathf.Atan(50 * (currentDepth - 0.875f)) + 0.5f;
                    }
                }
                else if (deviceType == Device.DeviceType.TYPE_PERIPHERAL)
                {
                    // Depth: Between 10cm to 60cm preferred, up to 80cm maximum
                    // Field Of View: 140 x 120 degrees typical
                    float currentDepth = relativeHandPos.y;
                    float requiredWidthX = (currentDepth / 2) / Mathf.Sin(Mathf.Deg2Rad * 120 / 2);
                    float requiredWidthY = (currentDepth / 2) / Mathf.Sin(Mathf.Deg2Rad * 140 / 2);
                    sigmaX = 0.2f * requiredWidthX;
                    sigmaY = 0.2f * requiredWidthY;

                    // amplitude should be 1 within ideal depth and go 'smoothly' to zero on both sides of the ideal depth.
                    if (currentDepth > 0.1f && currentDepth < 0.6f)
                    {
                        a = 1f;
                    }
                    else if (currentDepth < 0.1f)
                    {
                        a = 0.55f / (Mathf.PI / 2) * Mathf.Atan(100 * (currentDepth + 0.05f)) + 0.5f;
                    }
                    else if (currentDepth > 0.6f)
                    {
                        a = -0.55f / (Mathf.PI / 2) * Mathf.Atan(50 * (currentDepth - 0.7f)) + 0.5f;
                    }
                }
            }

            float confidence = a * Mathf.Exp(-(Mathf.Pow(x - x0, 2) / (2 * Mathf.Pow(sigmaX, 2)) + Mathf.Pow(y - y0, 2) / (2 * Mathf.Pow(sigmaY, 2))));

            if (confidence < 0) confidence = 0;

            return confidence;
        }

        /// <summary>
        /// uses the palm normal relative to the direction from hand to device to calculate a confidence
        /// </summary>
        float Confidence_RelativeHandRot(Transform deviceOrigin, Vector3 handPos, Vector3 palmNormal)
        {
            // angle between palm normal and the direction from hand pos to device origin
            float palmAngle = Vector3.Angle(palmNormal, deviceOrigin.position - handPos);

            // get confidence based on a cos where it should be 1 if the angle is 0 or 180 degrees,
            // and it should be 0 if it is 90 degrees
            float confidence = (Mathf.Cos(Mathf.Deg2Rad * 2 * palmAngle) + 1f) / 2;

            //Debug.Log(confidence);
            return confidence;
        }

        /// <summary>
        /// uses the hand velocity to calculate a confidence.
        /// returns a high confidence, if the velocity is low, and a low confidence otherwise.
        /// Returns 0, if the hand hasn't been consistently tracked for about the last 10 frames
        /// </summary>
        float Confidence_RelativeHandVelocity(LeapProvider provider, Transform deviceOrigin, Vector3 handPos, bool isLeft)
        {
            Vector3 oldPosition;
            float oldTime;

            bool positionsRecorded = isLeft ? lastLeftHandPositions[provider].GetOldestPosition(out oldPosition, out oldTime) : lastRightHandPositions[provider].GetOldestPosition(out oldPosition, out oldTime);

            // if we haven't recorded any positions yet, or the hand hasn't been present in the last 10 frames (oldest position is older than 10 * frame time), return 0
            if (!positionsRecorded || (Time.time - oldTime) > Time.deltaTime * 10)
            {
                return 0;
            }

            float velocity = Vector3.Distance(handPos, oldPosition) / (Time.time - oldTime);

            float confidence = 0;
            if (velocity < 2)
            {
                confidence = -0.5f * velocity + 1;
            }

            return confidence;
        }

        float Confidence_LengthHandVisible(LeapProvider provider, bool isLeft)
        {
            if ((isLeft ? leftHandFirstVisible[provider] : rightHandFirstVisible[provider]) == 0)
            {
                return 0;
            }

            float lengthVisible = Time.time - (isLeft ? leftHandFirstVisible[provider] : rightHandFirstVisible[provider]);

            float confidence = 1;
            if (lengthVisible < 1)
            {
                confidence = lengthVisible;
            }

            return confidence;
        }
        #endregion

        #region Joint Confidence Methods

        /// <summary>
        /// uses the normal vector of a joint / bone (outwards pointing one) and the direction from joint to device 
        /// to calculate per-joint confidence values
        /// </summary>
        float[] Confidence_RelativeJointRot(Transform deviceOrigin, Hand hand)
        {
            float[] confidences = new float[VectorHand.NUM_JOINT_POSITIONS];

            foreach (var finger in hand.Fingers)
            {
                for (int j = 0; j < 4; j++)
                {
                    int key = (int)finger.Type * 4 + j;

                    Vector3 jointPos = finger.Bone((Bone.BoneType)j).NextJoint.ToVector3();
                    Vector3 jointNormalVector = new Vector3();
                    if ((int)finger.Type == 0) jointNormalVector = finger.Bone((Bone.BoneType)j).Rotation.ToQuaternion() * Vector3.right;
                    else jointNormalVector = finger.Bone((Bone.BoneType)j).Rotation.ToQuaternion() * Vector3.up * -1;

                    float angle = Vector3.Angle(jointPos - deviceOrigin.position, jointNormalVector);


                    // get confidence based on a cos where it should be 1 if the angle is 0 or 180 degrees,
                    // and it should be 0 if it is 90 degrees
                    confidences[key] = (Mathf.Cos(Mathf.Deg2Rad * 2 * angle) + 1f) / 2;
                }
            }
            // in the capsule hands joint 21 is copied and mirrored from joint 0
            confidences[21] = confidences[0];

            return confidences;
        }

        /// <summary>
        /// uses the normal vector of a joint / bone (outwards pointing one) and the palm normal vector
        /// to calculate per-joint confidence values
        /// </summary>
        float[] Confidence_relativeJointRotToPalmRot(Transform deviceOrigin, Hand hand)
        {
            float[] confidences = new float[VectorHand.NUM_JOINT_POSITIONS];

            foreach (var finger in hand.Fingers)
            {
                for (int j = 0; j < 4; j++)
                {
                    int key = (int)finger.Type * 4 + j;

                    Vector3 jointPos = finger.Bone((Bone.BoneType)j).NextJoint.ToVector3();
                    Vector3 jointNormalVector = new Vector3();
                    jointNormalVector = finger.Bone((Bone.BoneType)j).Rotation.ToQuaternion() * Vector3.up * -1;

                    float angle = Vector3.Angle(hand.PalmNormal.ToVector3(), jointNormalVector);


                    // get confidence based on a cos where it should be 1 if the angle is 0,
                    // and it should be 0 if the angle is 180 degrees
                    confidences[key] = (Mathf.Cos(Mathf.Deg2Rad * angle) + 1f) / 2;
                }
            }
            // in the capsule hands joint 21 is copied and mirrored from joint 0
            confidences[21] = confidences[0];

            return confidences;
        }

        #endregion

        #region Helper Methods

        // small class to save hand positions from old frames along with a timestamp
        class LastHandPositions
        {
            LeapProvider provider;
            Vector3[] positions;
            float[] times;
            int index;

            public LastHandPositions()
            {
                this.positions = new Vector3[10];
                this.times = new float[10];
                this.index = 0;
            }

            public void ClearAllPositions()
            {
                positions.ClearWith(Vector3.zero);
            }

            public void AddPosition(Vector3 position, float time)
            {
                positions[index] = position;
                times[index] = time;
                index = (index + 1) % 10;
            }

            public bool GetPastPosition(int pastIndex, out Vector3 position, out float time)
            {
                position = positions[(index - 1 - pastIndex + 10) % 10];
                time = times[(index - 1 - pastIndex + 10) % 10];

                if (position == null || position == Vector3.zero)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            public bool GetOldestPosition(out Vector3 position, out float time)
            {
                for (int i = 9; i >= 0; i--)
                {
                    if (GetPastPosition(i, out position, out time))
                    {
                        return true;
                    }
                }

                GetPastPosition(0, out position, out time);
                return false;
            }
        }

        /// <summary>
        /// add all hands in the frame given by frames[frameIdx] to the Dictionaries lastLeftHandPositions and lastRightHandPositions,
        /// and update leftHandFirstVisible and rightHandFirstVisible
        /// </summary>
        void AddFrameToLengthVisibleDicts(Frame[] frames, int frameIdx)
        {
            bool[] handsVisible = new bool[2];

            foreach (Hand hand in frames[frameIdx].Hands)
            {
                //Debug.Log(hand.Id);
                if (hand.IsLeft)
                {
                    handsVisible[0] = true;
                    if (leftHandFirstVisible[providers[frameIdx]] == 0)
                    {
                        leftHandFirstVisible[providers[frameIdx]] = Time.time;
                    }

                    if (!lastLeftHandPositions.ContainsKey(providers[frameIdx]))
                    {
                        lastLeftHandPositions.Add(providers[frameIdx], new LastHandPositions());
                    }

                    lastLeftHandPositions[providers[frameIdx]].AddPosition(hand.PalmPosition.ToVector3(), Time.time);
                }
                else
                {
                    handsVisible[1] = true;
                    if (rightHandFirstVisible[providers[frameIdx]] == 0)
                    {
                        rightHandFirstVisible[providers[frameIdx]] = Time.time;
                    }

                    if (!lastRightHandPositions.ContainsKey(providers[frameIdx]))
                    {
                        lastRightHandPositions.Add(providers[frameIdx], new LastHandPositions());
                    }

                    lastRightHandPositions[providers[frameIdx]].AddPosition(hand.PalmPosition.ToVector3(), Time.time);

                }
            }

            if (!handsVisible[0])
            {
                leftHandFirstVisible[providers[frameIdx]] = 0;
            }
            if (!handsVisible[1])
            {
                rightHandFirstVisible[providers[frameIdx]] = 0;
            }
        }

        /// <summary>
        /// visualize where the merged joint data comes from, by using the debugColors in the same order as the providers in the provider list.
        /// the color is then linearly interpolated based on the joint confidences
        /// </summary>
        void VisualizeMergedJoints(CapsuleHand hand, List<float[]> jointConfidences)
        {


            Color[] colors = hand.SphereColors;

            for (int i = 0; i < jointConfidences[0].Length; i++)
            {
                colors[i] = debugColors[0];

                for (int j = 1; j < jointConfidences.Count; j++)
                {
                    float lerpValue = jointConfidences.Take(j).Sum(x => x[i]) / jointConfidences.Take(j + 1).Sum(x => x[i]);
                    colors[i] = Color.Lerp(debugColors[j], colors[i], lerpValue);
                }
            }
            hand.SphereColors = colors;
            hand.SetIndividualSphereColors = true;
        }

        #endregion
    }
}