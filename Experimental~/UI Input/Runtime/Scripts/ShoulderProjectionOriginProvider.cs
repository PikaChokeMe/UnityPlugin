﻿using UnityEngine;

namespace Leap.Unity.InputModule
{
        /// <summary>
        /// Provides information about the current inferred shoulder position. Uses the head rotation internally, so can provide that information too
        /// </summary>
        internal class ShoulderProjectionOriginProvider : IProjectionOriginProvider
        {
            private readonly Camera _mainCamera;

            private Vector3 _oldCameraPos = Vector3.zero;
            private Quaternion _oldCameraRot = Quaternion.identity;

            public Quaternion CurrentRotation { get; private set; }
            public Vector3 ProjectionOriginRight { get; private set; } = Vector3.zero;
            public Vector3 ProjectionOriginLeft { get; private set; } = Vector3.zero;


            public ShoulderProjectionOriginProvider(Camera mainCamera)
            {
                _mainCamera = mainCamera;

                //Used for calculating the origin of the Projective Interactions
                if (mainCamera != null)
                {
                    CurrentRotation = mainCamera.transform.rotation;
                }
                else
                {
                    Debug.LogAssertion("Tag your Main Camera with 'MainCamera' for the UI Module");
                }
            }

            public void Update()
            {
                // Update the Head Yaw for Calculating "Shoulder Positions"
                if (_mainCamera != null)
                {
                    var transform = _mainCamera.transform;
                    _oldCameraPos = transform.position;
                    _oldCameraRot = transform.rotation;

                    var headYaw = Quaternion.Euler(0f, _oldCameraRot.eulerAngles.y, 0f);
                    CurrentRotation = Quaternion.Slerp(CurrentRotation, headYaw, 0.1f);
                }
            }

            public void Process()
            {
                if (_mainCamera != null)
                {
                    ProjectionOriginLeft = _oldCameraPos + CurrentRotation * new Vector3(-0.15f, -0.2f, 0f);
                    ProjectionOriginRight = _oldCameraPos + CurrentRotation * new Vector3(0.15f, -0.2f, 0f);
                }
            }

            /// <summary>
            /// Returns the projection origin for the specfied hand
            /// </summary>
            /// <param name="hand">Hand used to determine chirality</param>
            /// <returns>The projection origin to use for the specified hand</returns>
            public Vector3 ProjectionOriginForHand(Hand hand)
                => hand.IsLeft
                    ? ProjectionOriginLeft
                    : ProjectionOriginRight;
        }
}
