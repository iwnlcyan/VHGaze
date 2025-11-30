using RealisticEyeMovements;
using UnityEngine;

namespace RealisticEyeMovements
{
	public class GazeController : MonoBehaviour
	{
		#region fields

			[SerializeField] Transform sphereXform = null;

			public LookTargetController lookTargetController;

        #endregion

        void Awake()
		{
			//lookTargetController = FindObjectOfType<LookTargetController>();
		}
		

		public void OnLookAtPlayerSelected()
		{
			lookTargetController.LookAtPlayer();
            Debug.Log("OnLookAtPlayerSelected");
        }


		public void OnLookAtSphereSelected()
		{
			lookTargetController.LookAtPoiDirectly(sphereXform);
            Debug.Log("OnLookAtSphereSelected");
        }


		public void OnLookIdlySelected()
		{
			lookTargetController.LookAroundIdly();
			Debug.Log("OnLookIdlySelected");
		}
		
	}
}