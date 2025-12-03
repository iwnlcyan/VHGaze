using RealisticEyeMovements;
using UnityEngine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace RealisticEyeMovements
{
	public class GazeController : MonoBehaviour
	{
        #region fields
        // ====== External inputs (plug your providers here) ======
            [Header("Debug")]
            [SerializeField] Transform POI = null;
			public LookTargetController lookTargetController;

            // ====== External inputs (plug your providers here) ======
            [Header("Providers / Inputs")]
            [Tooltip("World-space transform for user's eyes (or midpoint between them).")]
            public Transform userEyes;
            [Tooltip("World-space transform for user's face anchor (nose/cheek area).")]
            public Transform userFace;
            [Tooltip("A list of candidate referents (task objects) with priorities.")]
            public List<Referent> referents = new List<Referent>();
            [Tooltip("Default idle anchor to lightly scan around (e.g., between user and horizon).")]
            public Transform idleAnchor;

            [Tooltip("Implement these callbacks to feed normalized cues in [0,1].")]
            public CuesProvider cues; // assign a component that implements the interface below

            //[Header("Animators (your own eye/head controllers)")]
            //public EyeHeadAnimator eyeHead; // expose methods: LookAt(Transform/direction), SetHeadEyeBlend, etc.

            // ====== DDM Core Settings ======
            [Header("DDM Settings")]
            [Tooltip("Max time to accumulate evidence per decision epoch (seconds).")]
            public float maxDecisionTime = 0.3f;
            [Tooltip("Additive Gaussian noise gain per step.")]
            public float noiseLevel = 0.05f;
            [Tooltip("Simulation substep for accumulation (seconds).")]
            public float ddmDt = 0.01f;

            [Tooltip("Decision thresholds per target (smaller = easier to choose).")]
            public float lambdaEyes = 0.48f;
            public float lambdaFace = 0.46f;
            public float lambdaRef = 0.44f;
            public float lambdaAvert = 0.42f;
            public float lambdaIdle = 0.60f;

            [Tooltip("Bias terms per target (positive biases tilt choice).")]
            public float biasEyes = 0.02f;   // affiliative style
            public float biasFace = 0.01f;   // shy/soft style
            public float biasRef = 0.02f;   // task-focused style
            public float biasAvert = 0.00f;
            public float biasIdle = 0.00f;

            [Tooltip("Inhibition-of-return window (seconds).")]
            public float inhibitionOfReturnTime = 0.9f;
            [Tooltip("IOR penalty applied multiplicatively when target was recently fixated.")]
            [Range(0.3f, 1f)] public float iorFactor = 0.3f;

            // ====== Dwell / Rendering Timing ======
            [Header("Fixation/Dwell Distributions (seconds)")]
            public Vector2 dwellMutualRange = new Vector2(2.30f, 4.80f);
            public Vector2 dwellFaceRange = new Vector2(2.50f, 6.60f);
            public Vector2 dwellRefRange = new Vector2(3.40f, 6.00f);
            public Vector2 dwellAvertRange = new Vector2(2.20f, 4.40f);
            public Vector2 dwellIdleRange = new Vector2(3.50f, 4.50f);

            [Header("Onset & Motion")]
            [Tooltip("Saccade onset latency jitter (s).")]
            public Vector2 onsetLatencyRange = new Vector2(0.15f, 0.25f);
            [Tooltip("Head-Eye blend per target: proportion of rotation driven by eyes (0..1).")]
            [Range(0, 1)] public float eyeBlendAffiliative = 0.7f;
            [Range(0, 1)] public float eyeBlendFaceSoft = 0.8f;
            [Range(0, 1)] public float eyeBlendReferential = 0.3f;
            [Range(0, 1)] public float eyeBlendAversion = 0.6f;
            [Range(0, 1)] public float eyeBlendIdle = 0.7f;

            //[Header("Saccadic Scanning (micro-movements)")]
            //public bool enableMicroSaccades = true;
            //public Vector2 microISI = new Vector2(0.15f, 0.30f);
            //public Vector2 microAmpDeg = new Vector2(0.5f, 2.5f);

            // ====== Runtime state ======
            private bool isFixating = false;
            private GazeTargetType currentType = GazeTargetType.IdleAnchor;
            private Transform currentTarget = null;

            // DDM accumulators and IOR memory
            private readonly System.Random rng = new System.Random();
            private readonly Dictionary<GazeTargetType, float> g = new Dictionary<GazeTargetType, float>();
            private readonly Dictionary<GazeTargetType, float> lastFixatedAgo = new Dictionary<GazeTargetType, float>();

        #endregion

        // Cache / temp
        private readonly List<GazeTargetType> allTargets = new List<GazeTargetType>{
        GazeTargetType.UserEyes, GazeTargetType.UserFace, GazeTargetType.Referent,
        GazeTargetType.Aversion, GazeTargetType.IdleAnchor
        };

        void Awake()
        {
            foreach (var t in allTargets)
            {
                g[t] = 0f;
                lastFixatedAgo[t] = 999f; // far in the past
            }
        }

        void Update()
        {
            if (isFixating)
            {
                // Still increment IOR timers
                foreach (var t in allTargets)
                    lastFixatedAgo[t] += Time.deltaTime;

                return;
            }

            // Run a short DDM epoch to choose next target
            var choice = RunDDMEpoch();
            // Resolve concrete Transform for the chosen target
            Transform targetXform = ResolveTargetTransform(choice);
            if (targetXform == null) targetXform = idleAnchor;

            // Start fixation / rendering
            StartCoroutine(FocusRoutine(choice, targetXform));
        }

        // ---- DDM epoch: accumulate for <= maxDecisionTime; winner crosses threshold or argmax ----
        private GazeTargetType RunDDMEpoch()
        {
            // reset accumulators
            foreach (var ti in allTargets) g[ti] = 0f;

            float t = 0f;
            while (t < maxDecisionTime)
            {
                foreach (var target in allTargets)
                {
                    float mu = DriftFor(target);      // from social cues
                    mu *= IORWeight(target);          // inhibition-of-return
                    float b = BiasFor(target);
                    float n = (float)NextGaussian(0, 1) * noiseLevel;

                    g[target] += (mu + n + b) * ddmDt;

                    if (Mathf.Abs(g[target]) >= ThresholdFor(target))
                        return target; // first to cross
                }
                t += ddmDt;
            }

            // No threshold crossing: pick the max accumulator
            return g.OrderByDescending(kv => kv.Value).First().Key;
        }

        // ---- Map target type to Transform ----
        private Transform ResolveTargetTransform(GazeTargetType t)
        {
            switch (t)
            {
                case GazeTargetType.UserEyes:
                    return userEyes ? userEyes : userFace;
                case GazeTargetType.UserFace:
                    return userFace ? userFace : userEyes;
                case GazeTargetType.Referent:
                    // pick top referent by current priority (already normalized in provider)
                    if (referents != null && referents.Count > 0)
                    {
                        var best = referents.OrderByDescending(r => r.CurrentPriority).First();
                        return best != null ? best.anchor : idleAnchor;
                    }
                    return idleAnchor;
                case GazeTargetType.Aversion:
                    return GetAversionAnchor(); // compute a down/side offset target
                case GazeTargetType.IdleAnchor:
                default:
                    return idleAnchor;
            }
        }

        // ---- Fixation routine (handles onset latency, dwell, micro-saccades, head/eye blend) ----
        private IEnumerator FocusRoutine(GazeTargetType type, Transform target)
        {
            // LOCK the DDM
            isFixating = true;
            currentType = type;
            currentTarget = target;

            // ---- Onset latency ----
            float onset = UnityEngine.Random.Range(onsetLatencyRange.x, onsetLatencyRange.y);
            yield return new WaitForSeconds(onset);

            // ---- Execute your gaze behavior ----
            switch (type)
            {
                case GazeTargetType.UserEyes:
                    MutalGaze();
                    break;

                case GazeTargetType.UserFace:
                    OneSidedGaze();
                    break;

                case GazeTargetType.Referent:
                    ReferentialGaze();
                    break;

                case GazeTargetType.Aversion:
                    AvertedGaze();
                    break;

                case GazeTargetType.IdleAnchor:
                default:
                    AvertedGaze();
                    break;
            }

            // ---- Dwell time (THIS PART WAS MISSING!) ----
            float dwell = DwellFor(type);
            float elapsed = 0f;

            while (elapsed < dwell)
            {
                elapsed += Time.deltaTime;
                yield return null;   // <-- THIS PAUSES THE DDM LOOP
            }

            // ---- Update IOR memory ----
            lastFixatedAgo[type] = 0f;

            // ---- UNLOCK the DDM ----
            isFixating = false;
        }


        //private void ApplyMicroSaccadeAround(Transform anchor)
        //{
        //    // Small random offset around anchor direction
        //    float ampDeg = UnityEngine.Random.Range(microAmpDeg.x, microAmpDeg.y);
        //    Vector3 jitter = UnityEngine.Random.onUnitSphere * Mathf.Tan(ampDeg * Mathf.Deg2Rad);
        //    eyeHead.LookAtOffset(anchor, jitter); // implement in your animator as small eye-only nudge
        //}

        private float DwellFor(GazeTargetType type)
        {
            switch (type)
            {
                case GazeTargetType.UserEyes: return UnityEngine.Random.Range(dwellMutualRange.x, dwellMutualRange.y);
                case GazeTargetType.UserFace: return UnityEngine.Random.Range(dwellFaceRange.x, dwellFaceRange.y);
                case GazeTargetType.Referent: return UnityEngine.Random.Range(dwellRefRange.x, dwellRefRange.y);
                case GazeTargetType.Aversion: return UnityEngine.Random.Range(dwellAvertRange.x, dwellAvertRange.y);
                case GazeTargetType.IdleAnchor: return UnityEngine.Random.Range(dwellIdleRange.x, dwellIdleRange.y);
                default: return 0.4f;
            }
        }

        //private float EyeBlendFor(GazeTargetType type)
        //{
        //    switch (type)
        //    {
        //        case GazeTargetType.UserEyes: return eyeBlendAffiliative;
        //        case GazeTargetType.UserFace: return eyeBlendFaceSoft;
        //        case GazeTargetType.Referent: return eyeBlendReferential;
        //        case GazeTargetType.Aversion: return eyeBlendAversion;
        //        case GazeTargetType.IdleAnchor: return eyeBlendIdle;
        //        default: return 0.7f;
        //    }
        //}

        private float ThresholdFor(GazeTargetType t)
        {
            switch (t)
            {
                case GazeTargetType.UserEyes: return lambdaEyes;
                case GazeTargetType.UserFace: return lambdaFace;
                case GazeTargetType.Referent: return lambdaRef;
                case GazeTargetType.Aversion: return lambdaAvert;
                case GazeTargetType.IdleAnchor: return lambdaIdle;
                default: return 0.3f;
            }
        }

        private float BiasFor(GazeTargetType t)
        {
            switch (t)
            {
                case GazeTargetType.UserEyes: return biasEyes;
                case GazeTargetType.UserFace: return biasFace;
                case GazeTargetType.Referent: return biasRef;
                case GazeTargetType.Aversion: return biasAvert;
                case GazeTargetType.IdleAnchor: return biasIdle;
                default: return 0f;
            }
        }

        private float IORWeight(GazeTargetType t)
        {
            // If we looked at this recently (< IOR time), suppress drift
            return (lastFixatedAgo[t] < inhibitionOfReturnTime) ? iorFactor : 1f;
        }

        // --------- The key piece: DRIFT from social cues (normalized 0..1 inputs) ----------
        private float DriftFor(GazeTargetType t)
        {
            // Pull live cues (you implement these in your provider)
            float userLooksAtMe = cues?.UserIsLookingAtMe() ?? 0f;  // reciprocity
            float turnYieldCue = cues?.TurnYieldCue() ?? 0f;  // end-of-utterance/prosody
            float affiliationGoal = cues?.AffiliationGoal() ?? 0.5f;
            float cognitiveLoad = cues?.AgentCognitiveLoad() ?? 0f;
            float proximity = cues?.ProximityNormalized() ?? 0.5f; // 0 far .. 1 very close
            float deixis = cues?.UtteranceContainsDeixis() ?? 0f;  // referencing words
            float referentSal = cues?.BestReferentPriority() ?? 0f;  // salience/priority [0,1]
            float userSpeaking = cues?.UserIsSpeaking() ?? 0f;  // damp referential during user floor
            float comfortPrior = cues?.ComfortPrior() ?? 0.5f; // preference for softer face gaze

            switch (t)
            {
                case GazeTargetType.UserEyes:
                    // Affiliative mutual gaze: up with reciprocity & turn-yield, down with load & very close proximity
                    return
                        0.55f * userLooksAtMe +
                        0.25f * turnYieldCue +
                        0.20f * affiliationGoal -
                        0.25f * cognitiveLoad -
                        0.20f * proximity; // intimacy regulation

                case GazeTargetType.UserFace:
                    // One-sided / soft gaze: similar to eyes but cushioned by comfort prior
                    return
                        0.40f * userLooksAtMe +
                        0.15f * turnYieldCue +
                        0.25f * affiliationGoal +
                        0.20f * comfortPrior -
                        0.10f * cognitiveLoad;

                case GazeTargetType.Referent:
                    // Referential: lead before/while mentioning object; damp while user speaks
                    return
                        0.45f * deixis +
                        0.35f * referentSal +
                        0.20f * (1f - userSpeaking);

                case GazeTargetType.Aversion:
                    // Aversion: when thinking or mutual gaze has run long (approx via load or proximity)
                    float longMutual = cues?.LongMutualGazeTimer() ?? 0f; // normalized 0..1
                    return
                        0.45f * cognitiveLoad +
                        0.35f * longMutual +
                        0.20f * proximity;

                case GazeTargetType.IdleAnchor:
                    // Small baseline so idle wins only when others are weak
                    return 0.05f;

                default: return 0f;
            }
        }

        // ---- Helper: aversion anchor (down/side from current head forward) ----
        private Transform GetAversionAnchor()
        {
            // Create or reuse a child anchor that we offset down/side each time
            if (!_aversionAnchor)
            {
                GameObject go = new GameObject("AversionAnchor");
                _aversionAnchor = go.transform;
                _aversionAnchor.SetParent(transform);
            }
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            // Random small offset down or side (10–25 degrees)
            float deg = UnityEngine.Random.Range(10f, 25f) * Mathf.Deg2Rad;
            Vector3 dir = (UnityEngine.Random.value < 0.5f ? -transform.up : (UnityEngine.Random.value < 0.5f ? right : -right));
            Vector3 tgt = (fwd + Mathf.Tan(deg) * dir).normalized;
            _aversionAnchor.position = transform.position + tgt * 2f; // 2m ahead in that direction
            return _aversionAnchor;
        }
        private Transform _aversionAnchor;

        // ---- Utilities ----
        private double NextGaussian(double mu, double sigma)
        {
            // Box–Muller
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mu + sigma * z;
        }

        //--------Gaze Behaviors----------

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

    // ----------- Types & Interfaces ----------------

    public enum GazeTargetType { UserEyes, UserFace, Referent, Aversion, IdleAnchor }

    [Serializable]
    public class Referent
    {
        public Transform anchor;
        [Range(0f, 1f)] public float CurrentPriority = 0.5f; // set per-frame by your task logic
    }

    // Your animation layer. Implement eye/head look methods in your project.
    //public interface EyeHeadAnimator
    //{
    //    void LookAt(Transform t);
    //    void LookAtOffset(Transform t, Vector3 localOffsetDirection);
    //    void SetHeadEyeBlend(float eyeWeight); // 0 = head only, 1 = eyes only
    //}

    // Provide normalized cues in [0,1] each frame.
    public interface CuesProvider
    {
        float UserIsLookingAtMe();     // reciprocity probability
        float TurnYieldCue();          // end-of-utterance cue
        float AffiliationGoal();       // desired social closeness
        float AgentCognitiveLoad();    // thinking/processing
        float ProximityNormalized();   // 0 far .. 1 very close
        float UtteranceContainsDeixis();// object reference imminent/current
        float BestReferentPriority();  // salience of top referent
        float UserIsSpeaking();        // 1 if user holds floor
        float ComfortPrior();          // preference for face-soft gaze
        float LongMutualGazeTimer();   // normalized build-up when mutual is long
    }
}