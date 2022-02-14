/******************************************************************************
 * Copyright (C) Ultraleap, Inc. 2011-2021.                                   *
 *                                                                            *
 * Use subject to the terms of the Apache License 2.0 available at            *
 * http://www.apache.org/licenses/LICENSE-2.0, or another agreement           *
 * between Ultraleap and you, your company or other organization.             *
 ******************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Leap.Unity.HandsModule
{

    /// <summary>
    /// The HandBinder allows you to use your own hand models so that they follow the leap tracking data.
    /// You can bind your model by specifying transforms for the different joints and use the debug and fine tuning options to test and adjust it.
    /// </summary>
    [DisallowMultipleComponent]
    public class HandBinder : HandModelBase
    {
        /// <summary>
        /// The Leap Hand object this hand model represents.
        /// </summary>
        public Hand LeapHand;

        /// <summary> 
        /// The size of the debug gizmos 
        /// </summary>
        [Tooltip("The size of the debug gizmos")]
        public float GizmoSize = 0.004f;
        /// <summary> 
        /// The length of the elbow to maintain the correct offset from the wrist 
        /// </summary>
        [Tooltip("The length of the elbow to maintain the correct offset from the wrist")]
        public float ElbowLength;
        /// <summary> 
        /// The rotation offset that will be assigned to all the fingers 
        /// </summary>
        [Tooltip("The rotation offset that will be assigned to all the fingers")]
        public Vector3 GlobalFingerRotationOffset;
        /// <summary> 
        /// The rotation offset that will be assigned to the wrist 
        /// </summary>
        [Tooltip("The rotation offset that will be assigned to the wrist")]
        public Vector3 WristRotationOffset;
        /// <summary> 
        /// Set the assigned transforms to the leap hand during editor 
        /// </summary>
        [Tooltip("Set the assigned transforms to the leap hand during editor")]
        public bool SetEditorPose;
        /// <summary> 
        /// Set the assigned transforms to the same position as the Leap Hand 
        /// </summary>
        [Tooltip("Set the assigned transforms to the same position as the Leap Hand")]
        public bool SetPositions = true;

        /// <summary>
        /// Should the hand binder modify the scale of the hand
        /// </summary>
        public bool SetModelScale = true;

        /// <summary> 
        /// Show the Leap Hand in the scene 
        /// </summary>
        [Tooltip("Show the Leap Hand in the scene")]
        public bool DebugLeapHand = true;
        /// <summary> 
        /// Show the leap's rotation axis in the scene 
        /// </summary>
        [Tooltip("Show the leap's rotation axis in the scene")]
        public bool DebugLeapRotationAxis = false;
        /// <summary> 
        /// Show the assigned gameobjects as gizmos in the scene 
        /// </summary>
        [Tooltip("Show the assigned gameobjects as gizmos in the scene")]
        public bool DebugModelTransforms = true;
        /// <summary> 
        /// Show the assigned gameobjects rotation axis in the scene 
        /// </summary>
        [Tooltip("Show the assigned gameobjects rotation axis in the scene")]
        public bool DebugModelRotationAxis;

        /// <summary> 
        /// Used by the editor script. Fine tuning allows to specify custom wrist and 
        /// finger rotation offsets. 
        /// </summary>
        public bool FineTuning;
        /// <summary>  
        /// Used by the editor script. The DebugOptions allow to show a debug hand in the scene view
        /// and visualize its rotation and its attached gameobjects
        /// </summary>
        public bool DebugOptions;
        /// <summary> 
        /// Used by the editor script. 
        /// </summary>
        public bool EditPoseNeedsResetting = false;
        /// <summary> 
        /// The chirality or handedness of the hand.
        /// Custom editor requires Chirality in a non overridden property, Public Chirality exists for the editor.
        /// </summary>
        public Chirality Chirality;

        /// <summary> 
        /// The data structure that contains transforms that get bound to the leap data 
        /// </summary>
        public BoundHand BoundHand = new BoundHand();
        /// <summary> 
        /// User defined offsets in editor script 
        /// </summary>
        public List<BoundTypes> Offsets = new List<BoundTypes>();
        /// <summary> 
        /// Stores all the children's default pose 
        /// </summary>
        public SerializedTransform[] DefaultHandPose;

        /// <summary>
        /// The chirality or handedness of this hand (left or right).
        /// To set, change the public Chirality.
        /// </summary>
        public override Chirality Handedness { get { return Chirality; } set { } }
        /// <summary>
        /// The type of the Hand model (set to Graphics).
        /// </summary>
        public override ModelType HandModelType { get { return ModelType.Graphics; } }
        /// <summary>
        /// Returns the Leap Hand object represented by this HandModelBase. 
        /// Note that any physical quantities and directions obtained from the Leap Hand object are 
        /// relative to the Leap Motion coordinate system, which uses a right-handed axes and units 
        /// of millimeters.
        /// </summary>
        /// <returns></returns>
        public override Hand GetLeapHand() { return LeapHand; }
        /// <summary>
        /// Assigns a Leap Hand object to this HandModelBase.
        /// </summary>
        /// <param name="hand"></param>
        public override void SetLeapHand(Hand hand) { LeapHand = hand; }

        /// <summary>
        /// Returns whether or not this hand model supports editor persistence. 
        /// Set by public SetEditorPose.
        /// </summary>
        public override bool SupportsEditorPersistence()
        {
            bool editorPersistance = SetEditorPose;

            if (SetEditorPose == false)
            {
                ResetHand();
            }

            if (DebugLeapHand)
            {
                editorPersistance = true;
            }

            return editorPersistance;
        }

        private void OnDestroy()
        {
            ResetHand();
        }

        //Reset is called when the user hits the Reset button in the Inspector's context menu or when adding the component the first time.
        private void Reset()
        {

            //Return if we already have assigned base transforms
            if (DefaultHandPose != null)
            {
                ResetHand();
                return;
            }

            else
            {
                //Store all children transforms so the user has the ability to reset back to a default pose
                var allChildren = new List<Transform>();
                allChildren.Add(transform);
                allChildren.AddRange(HandBinderAutoBinder.GetAllChildren(transform));

                var baseTransforms = new List<SerializedTransform>();
                foreach (var child in allChildren)
                {
                    var serializedTransform = new SerializedTransform();
                    serializedTransform.reference = child.gameObject;
                    serializedTransform.transform = new TransformStore();
                    serializedTransform.transform.position = child.localPosition;
                    serializedTransform.transform.rotation = child.localRotation.eulerAngles;
                    baseTransforms.Add(serializedTransform);
                }
                DefaultHandPose = baseTransforms.ToArray();
            }
        }

        /// <summary>
        /// Called once per frame when the LeapProvider calls the event OnUpdateFrame.
        /// Update the BoundGameobjects so that the positions and rotations match that of the leap hand
        /// </summary>
        public override void UpdateHand()
        {
            if (!SetEditorPose && !Application.isPlaying)
            {
                return;
            }

            var hand = GetLeapHand();

            if (hand != null)
            {
                if (SetModelScale && SetPositions && BoundHand.startScale != Vector3.zero)
                {
                    transform.localScale = BoundHand.startScale * CalculateRatio(hand);
                }
                else
                {
                    transform.localScale = BoundHand.startScale;
                }

                if (BoundHand.elbow.boundTransform != null)
                {
                    if (SetPositions)
                    {
                        BoundHand.elbow.boundTransform.transform.position = hand.Arm.ElbowPosition.ToVector3();
                        BoundHand.elbow.boundTransform.transform.rotation = hand.Arm.Rotation.ToQuaternion() * Quaternion.Euler(BoundHand.elbow.offset.rotation) * Quaternion.Euler(WristRotationOffset);
                    }
                    //Calculate the elbows position and rotation making sure to maintain the models forearm length
                    else if (BoundHand.wrist.boundTransform != null)
                    {
                        var dir = (hand.Arm.PrevJoint.ToVector3() - hand.WristPosition.ToVector3()).normalized;
                        var position = hand.WristPosition.ToVector3() + dir * ElbowLength;
                        position += BoundHand.elbow.offset.position;

                        BoundHand.elbow.boundTransform.transform.position = position;
                        BoundHand.elbow.boundTransform.transform.rotation = hand.Arm.Rotation.ToQuaternion()
                            * Quaternion.Euler(BoundHand.elbow.offset.rotation)
                            * Quaternion.Euler(WristRotationOffset);
                    }
                }

                //Update the wrist's position and rotation to leap data
                if (BoundHand.wrist.boundTransform != null)
                {

                    //Calculate the position of the wrist to the leap position + offset defined by the user
                    var wristPosition = hand.WristPosition.ToVector3() + BoundHand.wrist.offset.position;

                    //Calculate rotation offset needed to get the wrist into the same rotation as the leap based on the calculated wrist offset
                    var leapRotationOffset = ((Quaternion.Inverse(BoundHand.wrist.boundTransform.transform.rotation) * hand.Rotation.ToQuaternion()) * Quaternion.Euler(WristRotationOffset)).eulerAngles;

                    //Set the wrist bone to the calculated values
                    BoundHand.wrist.boundTransform.transform.position = wristPosition;
                    BoundHand.wrist.boundTransform.transform.rotation *= Quaternion.Euler(leapRotationOffset);
                }

                //Loop through all the leap fingers and update the bound fingers to the leap data

                for (int fingerIndex = 0; fingerIndex < hand.Fingers.Count; fingerIndex++)
                {
                    for (int boneIndex = 0; boneIndex < hand.Fingers[fingerIndex].bones.Length; boneIndex++)
                    {

                        //The transform that the user has defined
                        var boundTransform = BoundHand.fingers[fingerIndex].boundBones[boneIndex].boundTransform;

                        //Continue if the user has not defined a transform for this finger
                        if (boundTransform == null)
                        {
                            continue;
                        }

                        //Get the start transform that was stored for each assigned transform
                        var startTransform = BoundHand.fingers[fingerIndex].boundBones[boneIndex].startTransform;

                        //Get the leap bone to extract the position and rotation values
                        var leapBone = hand.Fingers[fingerIndex].bones[boneIndex];
                        //Get any offsets the user has set up
                        var boneOffset = BoundHand.fingers[fingerIndex].boundBones[boneIndex].offset;

                        //Only update the finger position if the user has defined this behaviour
                        if (SetPositions)
                        {
                            boundTransform.transform.position = leapBone.PrevJoint.ToVector3();
                            boundTransform.transform.localPosition += boneOffset.position;
                        }

                        else
                        {
                            boundTransform.transform.localPosition = startTransform.position + boneOffset.position;
                        }

                        //Update the bound transforms rotation to the leap's rotation * global rotation offset * any further offsets the user has defined
                        boundTransform.transform.rotation = leapBone.Rotation.ToQuaternion() * Quaternion.Euler(GlobalFingerRotationOffset) * Quaternion.Euler(boneOffset.rotation);
                    }
                }

                EditPoseNeedsResetting = true;
            }

        }

        /// <summary>
        /// Compare the 3D models scale with the current leap data scale 
        /// </summary>
        float CalculateRatio(Hand hand)
        {
            return CalculateLeapSize(hand) / BoundHand.baseScale;
        }

        /// <summary>
        /// Calculate the leap hand size
        /// </summary>
        float CalculateLeapSize(Hand hand)
        {
            var length = 0f;
            for (int i = 0; i < hand.Fingers[(int)Finger.FingerType.TYPE_MIDDLE].bones.Length; i++)
            {
                var bone = hand.Fingers[(int)Finger.FingerType.TYPE_MIDDLE].bones[i];
                length += (bone.PrevJoint - bone.NextJoint).Magnitude;
            }
            return length;
        }

        /// <summary>
        /// Reset the boundGameobjects back to the default pose given by DefaultHandPose.
        /// </summary>
        public void ResetHand(bool forceReset = false)
        {

            if (DefaultHandPose == null || EditPoseNeedsResetting == false && forceReset == false)
            {
                return;
            };

            for (int i = 0; i < DefaultHandPose.Length; i++)
            {

                var baseTransform = DefaultHandPose[i];
                if (baseTransform != null && baseTransform.reference != null)
                {
                    baseTransform.reference.transform.localPosition = baseTransform.transform.position;
                    baseTransform.reference.transform.localRotation = Quaternion.Euler(baseTransform.transform.rotation);
                }
            }
            EditPoseNeedsResetting = false;
        }
    }
}