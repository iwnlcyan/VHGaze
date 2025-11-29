using UnityEngine;

namespace RealisticEyeMovements
{
    
	[HelpURL("https://docs.google.com/document/d/1b91EBehAyq_7GpTTxRHp1M5UbHfDQQ9CBektQgedXUg/edit?tab=t.0#bookmark=id.i1k51bltv63f")]
    [DefaultExecutionOrder(999999)]
    public class BlendshapesCopier : MonoBehaviour
    {
        #region fields
        
            [Tooltip("The main renderer whose blendshape values will be copied from (usually the character head or body renderer).")]
            [SerializeField] SkinnedMeshRenderer sourceRenderer;
            [Tooltip("The renderers whose blendshapes should follow those from the source renderer (eyebrows, beards, etc.).")]
            [SerializeField] SkinnedMeshRenderer[] targetRenderers;

            int[,] blendshapeIndices;
            int numSourceBlendshapes;
            
        #endregion

        
        void LateUpdate()
        {
            if ( sourceRenderer == null )
                return;

            for ( int sourceBlendshapeIndex=0;  sourceBlendshapeIndex<numSourceBlendshapes;  sourceBlendshapeIndex++ )
            {
                float sourceBlendShapeValue = sourceRenderer.GetBlendShapeWeight(sourceBlendshapeIndex);
                for ( int targetObjectIndex = 0;  targetObjectIndex<targetRenderers.Length;   targetObjectIndex++ )
                {
                    int targetBlendshapeIndex = blendshapeIndices[sourceBlendshapeIndex, targetObjectIndex];
                    if ( targetBlendshapeIndex >= 0 )
                        targetRenderers[targetObjectIndex].SetBlendShapeWeight(targetBlendshapeIndex, sourceBlendShapeValue);
                }
            }
        }


        void Start()
        {
            if ( sourceRenderer == null )
            {
                Transform ccBodyXform = transform.Find("CC_Base_Body");
                if ( ccBodyXform != null )
                    sourceRenderer = ccBodyXform.GetComponent<SkinnedMeshRenderer>();
            }
            
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            numSourceBlendshapes = sourceMesh.blendShapeCount;
            blendshapeIndices = new int[numSourceBlendshapes, targetRenderers.Length];
            for ( int i=0;  i<numSourceBlendshapes;  i++ )
            {
                string sourceBlendShapeName = sourceMesh.GetBlendShapeName(i);
                for (int targetObjectIndex=0;  targetObjectIndex<targetRenderers.Length;  targetObjectIndex++ )
                {
                    Mesh targetSharedMesh = targetRenderers[targetObjectIndex].sharedMesh;
                    int targetBlendShapeCount = targetSharedMesh.blendShapeCount;
                    for (int j = 0; j < targetBlendShapeCount; j++)
                    {
                        string targetBlendShapeName = targetSharedMesh.GetBlendShapeName(j);
                        blendshapeIndices[i, targetObjectIndex] = -1;
                        if (sourceBlendShapeName == targetBlendShapeName)
                        {
                            blendshapeIndices[i, targetObjectIndex] = j;
                            break;
                        }
                    }
                }
            }
        }

    }
}