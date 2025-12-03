using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RealisticEyeMovements;

// -------------------------------------------------------------
// GazeDDMController with Realistic Eye Movements Integration
// Multi-alternative Drift Diffusion Model for social gaze choice
// Integrated with EyeAndHeadAnimator for realistic eye/head animation
// -------------------------------------------------------------

public class GazeDDMController : MonoBehaviour
{
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
    public CuesProvider cues;

    [Header("Realistic Eye Movements Integration")]
    [Tooltip("Reference to the EyeAndHeadAnimator component.")]
    public EyeAndHeadAnimator eyeAndHeadAnimator;

    // ====== DDM Core Settings ======
    [Header("DDM Settings")]
    [Tooltip("Max time to accumulate evidence per decision epoch (seconds).")]
    public float maxDecisionTime = 0.2f;
    [Tooltip("Additive Gaussian noise gain per step.")]
    public float noiseLevel = 0.08f;
    [Tooltip("Simulation substep for accumulation (seconds).")]
    public float ddmDt = 0.01f;

    [Tooltip("Decision thresholds per target (smaller = easier to choose).")]
    public float lambdaEyes = 0.25f;
    public float lambdaFace = 0.26f;
    public float lambdaRef = 0.28f;
    public float lambdaAvert = 0.22f;
    public float lambdaIdle = 0.30f;

    [Tooltip("Bias terms per target (positive biases tilt choice).")]
    public float biasEyes = 0.02f;
    public float biasFace = 0.01f;
    public float biasRef = 0.01f;
    public float biasAvert = 0.00f;
    public float biasIdle = 0.00f;

    [Tooltip("Inhibition-of-return window (seconds).")]
    public float inhibitionOfReturnTime = 0.9f;
    [Tooltip("IOR penalty applied multiplicatively when target was recently fixated.")]
    [Range(0.3f, 1f)] public float iorFactor = 0.6f;

    // ====== Dwell / Rendering Timing ======
    [Header("Fixation/Dwell Distributions (seconds)")]
    public Vector2 dwellMutualRange = new Vector2(0.30f, 0.80f);
    public Vector2 dwellFaceRange = new Vector2(0.25f, 0.60f);
    public Vector2 dwellRefRange = new Vector2(0.40f, 0.90f);
    public Vector2 dwellAvertRange = new Vector2(0.20f, 0.60f);
    public Vector2 dwellIdleRange = new Vector2(0.25f, 0.50f);

    [Header("Head-Eye Coordination")]
    [Tooltip("Head latency per target type: negative = head leads, positive = eyes lead.")]
    public float headLatencyAffiliative = 0.075f;
    public float headLatencyFaceSoft = 0.05f;
    public float headLatencyReferential = -0.05f;
    public float headLatencyAversion = 0.1f;
    public float headLatencyIdle = 0.075f;

    [Header("Random Look Directions")]
    [Tooltip("Horizontal angle range for aversion looks (degrees).")]
    public Vector2 aversionHorizontalRange = new Vector2(-110f, 110f);
    [Tooltip("Vertical angle range for aversion looks (degrees).")]
    public Vector2 aversionVerticalRange = new Vector2(-18f, 12f);
    [Tooltip("Distance for aversion look point (meters).")]
    public float aversionLookDistance = 10f;

    [Tooltip("Horizontal angle range for idle looks (degrees).")]
    public Vector2 idleHorizontalRange = new Vector2(-45f, 45f);
    [Tooltip("Vertical angle range for idle looks (degrees).")]
    public Vector2 idleVerticalRange = new Vector2(-10f, 15f);
    [Tooltip("Distance for idle look point (meters).")]
    public float idleLookDistance = 8f;

    [Header("REM Micro-Saccade Control")]
    [Tooltip("Disable REM's random micro-saccades during DDM fixations.")]
    public bool suppressREMMicroSaccades = true;
    [Tooltip("Disable REM's random macro-saccades during DDM fixations.")]
    public bool suppressREMMacroSaccades = true;

    // ====== Runtime state ======
    private bool isFixating = false;
    private GazeTargetType currentType = GazeTargetType.IdleAnchor;
    private Transform currentTarget = null;

    // DDM accumulators and IOR memory
    private readonly System.Random rng = new System.Random();
    private readonly Dictionary<GazeTargetType, float> g = new Dictionary<GazeTargetType, float>();
    private readonly Dictionary<GazeTargetType, float> lastFixatedAgo = new Dictionary<GazeTargetType, float>();

    // Cache / temp
    private readonly List<GazeTargetType> allTargets = new List<GazeTargetType>{
        GazeTargetType.UserEyes, GazeTargetType.UserFace, GazeTargetType.Referent,
        GazeTargetType.Aversion, GazeTargetType.IdleAnchor
    };

    // Store original REM saccade settings
    private bool originalUseMicroSaccades;
    private bool originalUseMacroSaccades;

    void Awake()
    {
        foreach (var t in allTargets)
        {
            g[t] = 0f;
            lastFixatedAgo[t] = 999f;
        }
    }

    void Start()
    {
        // Auto-find EyeAndHeadAnimator if not assigned
        if (eyeAndHeadAnimator == null)
            eyeAndHeadAnimator = GetComponent<EyeAndHeadAnimator>();

        if (eyeAndHeadAnimator == null)
        {
            Debug.LogError("GazeDDMController: EyeAndHeadAnimator not found! Please assign it in the inspector.", this);
            enabled = false;
            return;
        }

        // Store original REM settings
        originalUseMicroSaccades = eyeAndHeadAnimator.useMicroSaccades;
        originalUseMacroSaccades = eyeAndHeadAnimator.useMacroSaccades;

        // Create idle anchor if not assigned
        if (idleAnchor == null)
        {
            GameObject idleGO = new GameObject("IdleAnchor");
            idleAnchor = idleGO.transform;
            idleAnchor.SetParent(transform);
            idleAnchor.position = transform.position + transform.forward * 3f;
        }
    }

    void Update()
    {
        // Update IOR timers
        foreach (var t in allTargets)
            lastFixatedAgo[t] += Time.deltaTime;

        if (isFixating)
            return;

        // Run a short DDM epoch to choose next target
        var choice = RunDDMEpoch();

        // Resolve concrete Transform for the chosen target
        Transform targetXform = ResolveTargetTransform(choice);
        if (targetXform == null)
            targetXform = idleAnchor;

        // Start fixation / rendering
        StartCoroutine(FocusRoutine(choice, targetXform));
    }

    // ---- DDM epoch: accumulate for <= maxDecisionTime; winner crosses threshold or argmax ----
    private GazeTargetType RunDDMEpoch()
    {
        // Reset accumulators
        foreach (var ti in allTargets)
            g[ti] = 0f;

        float t = 0f;
        while (t < maxDecisionTime)
        {
            foreach (var target in allTargets)
            {
                float mu = DriftFor(target);
                mu *= IORWeight(target);
                float b = BiasFor(target);
                float n = (float)NextGaussian(0, 1) * noiseLevel;

                g[target] += (mu + n + b) * ddmDt;

                if (Mathf.Abs(g[target]) >= ThresholdFor(target))
                    return target;
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
                if (referents != null && referents.Count > 0)
                {
                    var best = referents.OrderByDescending(r => r.CurrentPriority).First();
                    return best?.anchor ?? idleAnchor;
                }
                return idleAnchor;
            case GazeTargetType.Aversion:
                return GetAversionAnchor(); // Will be replaced by random point in FocusRoutine
            case GazeTargetType.IdleAnchor:
            default:
                return idleAnchor;
        }
    }

    // ---- Fixation routine using REM's LookAtSpecificThing ----
    private IEnumerator FocusRoutine(GazeTargetType type, Transform target)
    {
        isFixating = true;
        currentType = type;
        currentTarget = target;

        // Suppress REM's random saccades during our controlled fixation
        if (suppressREMMicroSaccades)
            eyeAndHeadAnimator.useMicroSaccades = false;
        if (suppressREMMacroSaccades)
            eyeAndHeadAnimator.useMacroSaccades = false;

        // Get head latency for this target type
        float headLatency = HeadLatencyFor(type);

        // Use REM's appropriate look function based on target type
        if (type == GazeTargetType.UserEyes)
        {
            eyeAndHeadAnimator.LookAtFace(userEyes, headLatency);
            Debug.Log("Mutual Gaze");
        }
        else if (type == GazeTargetType.UserFace)
        {
            eyeAndHeadAnimator.LookAtFace(userEyes, headLatency);
            Debug.Log("One Sided Gaze");
        }
        else if (type == GazeTargetType.Referent)
        {
            eyeAndHeadAnimator.LookAtSpecificThing(target, headLatency);
            Debug.Log("Referential Gaze");
        }
        else if (type == GazeTargetType.Aversion)
        {
            // Generate random aversion look direction
            Vector3 aversionPoint = GenerateRandomLookPoint(
                aversionHorizontalRange,
                aversionVerticalRange,
                aversionLookDistance
            );
            eyeAndHeadAnimator.LookAtAreaAround(aversionPoint, headLatency);
            Debug.Log($"Aversion - Looking at random point: {aversionPoint}");
        }
        else // IdleAnchor
        {
            // Generate random idle look direction
            Vector3 idlePoint = GenerateRandomLookPoint(
                idleHorizontalRange,
                idleVerticalRange,
                idleLookDistance
            );
            eyeAndHeadAnimator.LookAtAreaAround(idlePoint, headLatency);
            Debug.Log($"Idle - Looking at random point: {idlePoint}");
        }

        // Sample dwell time from distribution
        float dwell = DwellFor(type);

        // Wait for the full dwell duration
        yield return new WaitForSeconds(dwell);

        // Update IOR memory
        lastFixatedAgo[type] = 0f;

        // Restore REM's original saccade settings
        if (suppressREMMicroSaccades)
            eyeAndHeadAnimator.useMicroSaccades = originalUseMicroSaccades;
        if (suppressREMMacroSaccades)
            eyeAndHeadAnimator.useMacroSaccades = originalUseMacroSaccades;

        isFixating = false;
    }

    private float DwellFor(GazeTargetType type)
    {
        switch (type)
        {
            case GazeTargetType.UserEyes:
                return UnityEngine.Random.Range(dwellMutualRange.x, dwellMutualRange.y);
            case GazeTargetType.UserFace:
                return UnityEngine.Random.Range(dwellFaceRange.x, dwellFaceRange.y);
            case GazeTargetType.Referent:
                return UnityEngine.Random.Range(dwellRefRange.x, dwellRefRange.y);
            case GazeTargetType.Aversion:
                return UnityEngine.Random.Range(dwellAvertRange.x, dwellAvertRange.y);
            case GazeTargetType.IdleAnchor:
                return UnityEngine.Random.Range(dwellIdleRange.x, dwellIdleRange.y);
            default:
                return 0.4f;
        }
    }

    private float HeadLatencyFor(GazeTargetType type)
    {
        switch (type)
        {
            case GazeTargetType.UserEyes: return headLatencyAffiliative;
            case GazeTargetType.UserFace: return headLatencyFaceSoft;
            case GazeTargetType.Referent: return headLatencyReferential;
            case GazeTargetType.Aversion: return headLatencyAversion;
            case GazeTargetType.IdleAnchor: return headLatencyIdle;
            default: return 0.075f;
        }
    }

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
        return (lastFixatedAgo[t] < inhibitionOfReturnTime) ? iorFactor : 1f;
    }

    // --------- The key piece: DRIFT from social cues (normalized 0..1 inputs) ----------
    private float DriftFor(GazeTargetType t)
    {
        if (cues == null) return 0f;

        // Pull live cues
        float userLooksAtMe = cues.UserIsLookingAtMe();
        float turnYieldCue = cues.TurnYieldCue();
        float affiliationGoal = cues.AffiliationGoal();
        float cognitiveLoad = cues.AgentCognitiveLoad();
        float proximity = cues.ProximityNormalized();
        float deixis = cues.UtteranceContainsDeixis();
        float referentSal = cues.BestReferentPriority();
        float userSpeaking = cues.UserIsSpeaking();
        float userLooksAtTarget = cues.UserIsLookingAtTarget();
        float comfortPrior = cues.ComfortPrior();

        switch (t)
        {
            case GazeTargetType.UserEyes:
                return
                    0.55f * userLooksAtMe +
                    0.25f * turnYieldCue +
                    0.20f * affiliationGoal -
                    0.25f * cognitiveLoad -
                    0.20f * proximity;

            case GazeTargetType.UserFace:
                return
                    0.40f * userLooksAtMe +
                    0.15f * turnYieldCue +
                    0.25f * affiliationGoal +
                    0.20f * comfortPrior -
                    0.10f * cognitiveLoad;

            case GazeTargetType.Referent:
                return
                    (0.45f * deixis +
                    0.25f * referentSal) *
                    (1f - userSpeaking) +
                    0.45f * userLooksAtTarget * userSpeaking;

            case GazeTargetType.Aversion:
                float longMutual = cues.LongMutualGazeTimer();
                return
                    0.45f * cognitiveLoad +
                    0.35f * longMutual +
                    0.20f * proximity;

            case GazeTargetType.IdleAnchor:
                return 0.05f;

            default:
                return 0f;
        }
    }

    // ---- Helper: Generate random look point in world space ----
    private Vector3 GenerateRandomLookPoint(Vector2 horizontalRange, Vector2 verticalRange, float distance)
    {
        // Get character's eye center and head parent transform
        Vector3 eyeCenter = eyeAndHeadAnimator.GetOwnEyeCenter();
        Transform headParent = eyeAndHeadAnimator.GetHeadParentXform();

        // Random direction in local space
        float horizontalAngle = UnityEngine.Random.Range(horizontalRange.x, horizontalRange.y);
        float verticalAngle = UnityEngine.Random.Range(verticalRange.x, verticalRange.y);

        // Create a direction vector in local space
        Vector3 lookDirection = Quaternion.Euler(verticalAngle, horizontalAngle, 0) * Vector3.forward;

        // Convert to world space and create point at specified distance
        Vector3 worldLookDirection = headParent.TransformDirection(lookDirection);
        Vector3 lookPoint = eyeCenter + (worldLookDirection * distance);

        return lookPoint;
    }

    // ---- Helper: aversion anchor (deprecated - using random point generation instead) ----
    private Transform GetAversionAnchor()
    {
        if (!_aversionAnchor)
        {
            GameObject go = new GameObject("AversionAnchor");
            _aversionAnchor = go.transform;
            _aversionAnchor.SetParent(transform);
        }

        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;
        float deg = UnityEngine.Random.Range(10f, 25f) * Mathf.Deg2Rad;
        Vector3 dir = (UnityEngine.Random.value < 0.5f
            ? -transform.up
            : (UnityEngine.Random.value < 0.5f ? right : -right));
        Vector3 tgt = (fwd + Mathf.Tan(deg) * dir).normalized;
        _aversionAnchor.position = transform.position + tgt * 2f;

        return _aversionAnchor;
    }
    private Transform _aversionAnchor;

    // ---- Utilities ----
    private double NextGaussian(double mu, double sigma)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mu + sigma * z;
    }

    void OnDestroy()
    {
        // Restore REM settings on cleanup
        if (eyeAndHeadAnimator != null)
        {
            eyeAndHeadAnimator.useMicroSaccades = originalUseMicroSaccades;
            eyeAndHeadAnimator.useMacroSaccades = originalUseMacroSaccades;
        }
    }
}

// ----------- Types & Interfaces ----------------

public enum GazeTargetType { UserEyes, UserFace, Referent, Aversion, IdleAnchor }

[Serializable]
public class Referent
{
    public Transform anchor;
    [Range(0f, 1f)] public float CurrentPriority = 0.5f;
}

// Provide normalized cues in [0,1] each frame.
public interface CuesProvider
{
    float UserIsLookingAtMe();
    float TurnYieldCue();
    float AffiliationGoal();
    float AgentCognitiveLoad();
    float ProximityNormalized();
    float UtteranceContainsDeixis();
    float BestReferentPriority();
    float UserIsSpeaking();
    float UserIsLookingAtTarget();
    float ComfortPrior();
    float LongMutualGazeTimer();
}