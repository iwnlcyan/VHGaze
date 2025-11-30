using RealisticEyeMovements;
using UnityEngine;

namespace RealisticEyeMovements
{
	public class GazeController : MonoBehaviour
	{
		#region fields

			[SerializeField] Transform POI = null;

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
			lookTargetController.LookAtPoiDirectly(POI);
            Debug.Log("OnLookAtSphereSelected");
        }


		public void OnLookIdlySelected()
		{
			lookTargetController.Aversion();
			Debug.Log("Aversion");
		}

        public void MutalGaze()
        {
            lookTargetController.LookAtPlayer();
            Debug.Log("Mutual Gaze");
        }

        public void OneSidedGaze()
        {
            lookTargetController.LookAtPlayer();
            Debug.Log("One-Sided Gaze");
        }

        public void ReferentialGaze()
        {
            lookTargetController.LookAtPoiDirectly(POI);
            Debug.Log("Referential Gaze");
        }

        public void AvertedGaze()
        {
            lookTargetController.LookAroundIdly();
            Debug.Log("Averted Gaze");
        }

        //public void SaccadicGaze()
        //{
        //    lookTargetController.LookAroundIdly();
        //    Debug.Log("Saccadic Gaze");
        //}
    }
}