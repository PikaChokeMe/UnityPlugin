using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Leap.Unity;
using System.Linq;
using Leap;
using Leap.Unity.Encoding;

public class JointOcclusion : MonoBehaviour
{
    public Shader replacementShader;
    public CapsuleHand occlusionHand;

    Camera camera;

    Texture2D tex;
    Rect regionToReadFrom;
    Color[] occlusionSphereColors;
    Mesh cubeMesh;
    Material cubeMaterial;
    string layerName;

    // Start is called before the first frame update
    void Start()
    {
        List<JointOcclusion> allJointOcclusions = FindObjectsOfType<JointOcclusion>().ToList();
        layerName = "JointOcclusion" + allJointOcclusions.IndexOf(this).ToString();

        camera = GetComponent<Camera>();
        camera.SetReplacementShader(replacementShader, "RenderType");
        camera.cullingMask = LayerMask.GetMask(layerName);

        tex = new Texture2D(camera.targetTexture.width, camera.targetTexture.height);
        regionToReadFrom = new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height);

        occlusionHand.gameObject.layer = LayerMask.NameToLayer(layerName);
        occlusionHand.SetIndividualSphereColors = true;

        occlusionSphereColors = occlusionHand.SphereColors;

        for (int i = 0; i < occlusionSphereColors.Length; i++)
        {
            occlusionSphereColors[i] = Color.Lerp(Color.red, Color.green, (float)i / occlusionSphereColors.Length);
        }
        occlusionHand.SphereColors = occlusionSphereColors;


        cubeMesh = createCubeMesh();
        cubeMaterial = new Material(Shader.Find("Standard"));

    }

    private Mesh createCubeMesh()
    {
        Mesh cubeMesh = new Mesh();

        Vector3[] vertices = {
            new Vector3 (0, 0, 0),
            new Vector3 (0.5f, 0, 0),
            new Vector3 (0.5f, 0.5f, 0),
            new Vector3 (0, 0.5f, 0),
            new Vector3 (0, 0.5f, 0.5f),
            new Vector3 (0.5f, 0.5f, 0.5f),
            new Vector3 (0.5f, 0, 0.5f),
            new Vector3 (0, 0, 0.5f),
        };

        int[] triangles = {
         0, 2, 1, //face front
         0, 3, 2,
         2, 3, 4, //face top
         2, 4, 5,
         1, 2, 5, //face right
         1, 5, 6,
         0, 7, 4, //face left
         0, 4, 3,
         5, 4, 7, //face back
         5, 7, 6,
         0, 6, 7, //face bottom
         0, 1, 6
         };

        cubeMesh.vertices = vertices;
        cubeMesh.triangles = triangles;
        cubeMesh.RecalculateNormals();

        return cubeMesh;
    }

    public float[] Confidence_JointOcclusion(float[] confidences, Transform deviceOrigin, Hand hand)
    {
        if (hand == null)
        {
            return confidences.ClearWith(0);
        }

        Vector3 posOffset = new Vector3(-0.03f, 0.005f, -0.045f);
        Quaternion rotOffset = Quaternion.Euler(-5.366f, 0, 0);
        Vector3 scale = new Vector3(0.1f, 0.01f, 0.13f);

        //Graphics.DrawMeshNow(cubeMesh, Matrix4x4.TRS(hand.PalmPosition.ToVector3(), hand.Rotation.ToQuaternion(), Vector3.one), cubeMaterial, );
        Graphics.DrawMesh(cubeMesh, Matrix4x4.TRS(hand.PalmPosition.ToVector3() + hand.Direction.ToVector3() * posOffset.z + hand.PalmNormal.ToVector3() * posOffset.y + Vector3.Cross(hand.Direction.ToVector3(), hand.PalmNormal.ToVector3()) * posOffset.x, hand.Rotation.ToQuaternion() * rotOffset, scale), cubeMaterial, LayerMask.NameToLayer(layerName));

        camera.Render();
        RenderTexture.active = camera.targetTexture;

        tex.ReadPixels(regionToReadFrom, 0, 0);
        tex.Apply();


        int[] pixelCounts = new int[confidences.Length];

        //var pixels = tex.GetPixels();
        //for (int i = 0; i < occlusionSphereColors.Length; i++)
        //{
        //    pixelCounts[i] = pixels.Where(x => DistanceBetweenColors(x, occlusionSphereColors[i]) < 0.01f).Count();
        //}

        int[] jointPixelCount = new int[confidences.Length];
        foreach (var finger in hand.Fingers)
        {
            for (int j = 0; j < 4; j++)
            {
                // the capsule hands don't use metacarpal bones, so we don't get a confidence for them
                int key = (int)finger.Type * 5 + j + 1;
                int capsuleHandKey = (int)finger.Type * 4 + j;

                //jointDistances[(int)finger.Type * 4 + j]
                //float jointDistance = deviceOrigin.InverseTransformPoint(finger.Bone((Leap.Bone.BoneType)j).NextJoint.ToVector3()).y;
                //float jointDistance = Vector3.Distance(finger.Bone((Leap.Bone.BoneType)j).NextJoint.ToVector3(), capsuleHand.leapProvider.transform.position);


                // get projected sphere radius
                float jointRadius = 0.008f;
                //float radius = 1f / Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView) * jointRadius / Mathf.Sqrt(jointDistance * jointDistance - jointRadius * jointRadius);

                Vector3 jointPos = finger.Bone((Leap.Bone.BoneType)j).NextJoint.ToVector3();
                Vector3 screenPosCenter = camera.WorldToScreenPoint(jointPos);
                Vector3 screenPosSphereOutside = camera.WorldToScreenPoint(jointPos + camera.transform.right * jointRadius);

                float radius = new Vector2(screenPosSphereOutside.x - screenPosCenter.x, screenPosSphereOutside.y - screenPosCenter.y).magnitude;
                //radius *= 3.42f * tex.height / 2;

                jointPixelCount[key] = (int)(Mathf.PI * radius * radius);


                // only count pixels around where the sphere is supposed to be (+5 pixel margin)
                int margin = 5;
                int x0 = Mathf.Clamp((int)(screenPosCenter.x - radius - margin), 0, tex.width);
                int y0 = Mathf.Clamp((int)(screenPosCenter.y - radius - margin), 0, tex.height);
                int width = Mathf.Clamp((int)(screenPosCenter.x + radius + margin), 0, tex.width) - x0;
                int height = Mathf.Clamp((int)(screenPosCenter.y + radius + margin), 0, tex.height) - y0;

                //Debug.Log(x0 + ", " + y0 + ", " + width + ", " + height);

                Color[] tempPixels = tex.GetPixels(x0, y0, width, height);
                pixelCounts[key] = tempPixels.Where(x => DistanceBetweenColors(x, occlusionSphereColors[capsuleHandKey]) < 0.01f).Count();


                // get the pixel where the mid of the sphere is rendered to

                //if (key == 3)
                //{
                //    // only count pixels around where the sphere is supposed to be (+5 pixel margin)
                //    int margin = 5;
                //    int x0 = Mathf.Max(0, (int)(screenPosCenter.x - radius - margin));
                //    int y0 = Mathf.Max(0, (int)(screenPosCenter.y - radius - margin));
                //    int width = Mathf.Min(tex.width, (int)(screenPosCenter.x + radius + margin)) - x0;
                //    int height = Mathf.Min(tex.height, (int)(screenPosCenter.y + radius + margin)) - y0;

                //    Color[] tempPixels = tex.GetPixels(x0, y0, width, height);
                //    int count = tempPixels.Where(x => DistanceBetweenColors(x, occlusionSphereColors[key]) < 0.01f).Count();

                //    Debug.Log(radius + ". " + jointPixelCount[key] + "; " + pixelCounts[key] + "; " + count);
                //}

            }
        }


        for(int i = 0; i < confidences.Length; i++)
        {
            if (jointPixelCount[i] != 0)
            {
                confidences[i] = (float)pixelCounts[i] / jointPixelCount[i];
            }
        }

        // in the capsule hands joint 21 is copied and mirrored from joint 0
        confidences[21] = confidences[0];


        return confidences;
    }

    private void Update()
    {
        //float[] confidences = new float[VectorHand.NUM_JOINT_POSITIONS];
        //confidences = Confidence_JointOcclusion(confidences, transform, occlusionHand.GetLeapHand());

        //Color[] sphereColors = debugHand.SphereColors;
        //for (int i = 0; i < confidences.Length; i++)
        //{
        //    sphereColors[i] = Color.Lerp(Color.black, Color.white, confidences[i]);
        //}
        //debugHand.SphereColors = sphereColors;
    }

    float DistanceBetweenColors(Color color1, Color color2)
    {
        Color colorDifference = color1 - color2;
        Vector3 diffVetor = new Vector3(colorDifference.r, colorDifference.g, colorDifference.b);
        return diffVetor.magnitude;
    }

}
