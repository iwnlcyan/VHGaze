using RealisticEyeMovements;
using UnityEngine;

/// <summary>
/// Example implementation of the CuesProvider interface.
/// This version uses simple heuristics + placeholders so the DDM gaze controller
/// can work immediately. Later, you can replace these with real sensors:
/// • Eye tracking
/// • ASR / speech detection
/// • Conversation state models
/// • Proximity sensors
/// • Dialogue / task state
/// </summary>
public class GazeCuesProvider : MonoBehaviour, CuesProvider
{
    [Header("External Inputs (Optional)")]
    public Transform userHead;
    public Transform agentHead;

    [Tooltip("Simulated: probability the user is looking at the agent's eyes.")]
    [Range(0f, 1f)] public float simulatedUserLookingAtMe = 0.3f;

    [Tooltip("Simulated: likelihood agent is yielding the turn (ASR end-pointing).")]
    [Range(0f, 1f)] public float simulatedTurnYield = 0f;

    [Tooltip("Simulated: desire for social closeness (0=task, 1=chat).")]
    [Range(0f, 1f)] public float simulatedAffiliationGoal = 0.6f;

    [Tooltip("Simulated cognitive load (agent thinking, planning response).")]
    [Range(0f, 1f)] public float simulatedCognitiveLoad = 0.2f;

    [Tooltip("Simulated user speaking flag (replace with voice activity detection).")]
    [Range(0f, 1f)] public float simulatedUserSpeaking = 0f;

    [Tooltip("Simulated deixis (agent referencing an object, e.g., saying 'this').")]
    [Range(0f, 1f)] public float simulatedDeixis = 0f;

    [Tooltip("Simulated comfort prior (1 = prefers soft gaze on face, 0 = strong eye contact).")]
    [Range(0f, 1f)] public float simulatedComfortPrior = 0.5f;

    [Tooltip("Simulated referent priority (task object relevance).")]
    [Range(0f, 1f)] public float simulatedReferentPriority = 0f;

    [Tooltip("Internal mutual gaze timer (normalized).")]
    [Range(0f, 1f)] public float simulatedLongMutualGaze = 0f;

    // For computing proximity cue
    public float minProximity = 0.8f;  // distance at which proximity = 1
    public float maxProximity = 3.0f;  // distance at which proximity = 0

    void Update()
    {
        // Optional: automatically increase "long mutual gaze"
        if (simulatedUserLookingAtMe > 0.7f)
            simulatedLongMutualGaze = Mathf.Clamp01(simulatedLongMutualGaze + Time.deltaTime * 0.3f);
        else
            simulatedLongMutualGaze = Mathf.Clamp01(simulatedLongMutualGaze - Time.deltaTime * 0.5f);
    }

    // ---------------------------
    // Interface Implementations
    // ---------------------------

    public float UserIsLookingAtMe()
    {
        // Real version: eye-tracker gaze-ray probability
        return simulatedUserLookingAtMe;
    }

    public float TurnYieldCue()
    {
        // Real version: ASR end-of-utterance probability
        return simulatedTurnYield;
    }

    public float AffiliationGoal()
    {
        // Real version: dialogue manager state (social vs task)
        return simulatedAffiliationGoal;
    }

    public float AgentCognitiveLoad()
    {
        // Real version: internal agent state (NLP load, planning, time-to-respond)
        return simulatedCognitiveLoad;
    }

    public float ProximityNormalized()
    {
        if (!userHead || !agentHead) return 0.5f;

        float d = Vector3.Distance(userHead.position, agentHead.position);
        return Mathf.InverseLerp(maxProximity, minProximity, d);
    }

    public float UtteranceContainsDeixis()
    {
        // Real version: NLP tagger detects deictic phrases ("this", "that", "over here")
        return simulatedDeixis;
    }

    public float BestReferentPriority()
    {
        // Real version: object salience / task state / pointing detection
        return simulatedReferentPriority;
    }

    public float UserIsSpeaking()
    {
        // Real version: voice activity detection
        return simulatedUserSpeaking;
    }

    public float ComfortPrior()
    {
        // Real version: cultural preference or personalized user model
        return simulatedComfortPrior;
    }

    public float LongMutualGazeTimer()
    {
        // Real version: timer that increases if mutual gaze sustained > 1s
        return simulatedLongMutualGaze;
    }
}

