using RealisticEyeMovements;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Simple CuesProvider implementation with real eye tracking integration.
/// Uses Varjo eye tracking to detect if user is looking at agent's face.
/// </summary>
public class GazeCuesProvider : MonoBehaviour, CuesProvider
{
    [Header("External Inputs")]
    public Transform userHead;
    public Transform agentHead;

    [Header("Eye Tracking Integration")]
    [Tooltip("Reference to the EyeTrackingExample component for gaze data.")]
    public EyeTrackingExample eyeTrackingExample;

    [Tooltip("Agent's face center transform to detect gaze hits.")]
    public Transform agentFaceCenter;

    [Tooltip("Distance threshold for detecting gaze on agent's face (meters).")]
    public float gazeHitDistanceThreshold = 0.25f;

    [Tooltip("Distance threshold for detecting user looking at referent objects (meters).")]
    public float referentGazeThreshold = 0.4f;

    [Tooltip("Smoothing factor for detection (0=instant, 1=very smooth).")]
    [Range(0f, 0.95f)] public float smoothingFactor = 0.7f;

    [Header("ASR / Turn-Taking")]
    [Tooltip("Key to toggle microphone (simulates push-to-talk).")]
    public KeyCode micToggleKey = KeyCode.Space;

    [Tooltip("Reference to character's audio source for detecting when agent is speaking.")]
    public AudioSource agentVoice;

    [Tooltip("Time after mic stops before turn yield cue decays (seconds).")]
    public float turnYieldDecayTime = 0.5f;

    [Tooltip("Time while holding mic before user is considered 'speaking' (seconds).")]
    public float speakingStartDelay = 0.2f;

    [Header("Simulated Cues (for other inputs)")]
    [Range(0f, 1f)] public float simulatedUserLookingAtMe = 0.3f;
    [Range(0f, 1f)] public float simulatedTurnYield = 0f;
    [Range(0f, 1f)] public float simulatedAffiliationGoal = 0.6f;
    [Range(0f, 1f)] public float simulatedCognitiveLoad = 0.2f;
    [Range(0f, 1f)] public float simulatedUserSpeaking = 0f;
    [Range(0f, 1f)] public float simulatedUserLookingAtTarget = 0f;
    [Range(0f, 1f)] public float simulatedDeixis = 0f;
    [Range(0f, 1f)] public float simulatedComfortPrior = 0.5f;
    [Range(0f, 1f)] public float simulatedReferentPriority = 0f;

    [Header("Proximity Settings")]
    public float minProximity = 0.8f;
    public float maxProximity = 3.0f;

    // Runtime state - Eye Tracking
    private float userLookingAtMeValue = 0f;
    private float userLookingAtTargetValue = 0f;
    private float mutualGazeTimer = 0f;

    // Runtime state - ASR / Turn-Taking
    private bool isMicActive = false;
    private bool wasMicActiveLastFrame = false;
    private float turnYieldValue = 0f;
    private float userSpeakingValue = 0f;
    private float timeMicHeld = 0f;
    private float timeSinceMicReleased = 0f;

    void Start()
    {
        // Auto-find components if not assigned
        if (eyeTrackingExample == null)
            eyeTrackingExample = FindObjectOfType<EyeTrackingExample>();

        if (eyeTrackingExample == null)
            Debug.LogWarning("GazeCuesProvider: EyeTrackingExample not found!");

        if (agentFaceCenter == null && agentHead != null)
        {
            agentFaceCenter = agentHead;
            Debug.Log("GazeCuesProvider: Using agentHead as face center.");
        }

        if (agentVoice == null)
            Debug.LogWarning("GazeCuesProvider: AgentVoice not assigned. User speaking detection may not work correctly.");
    }

    void Update()
    {
        UpdateEyeTracking();
        UpdateASRTurnTaking();
    }

    /// <summary>
    /// Updates eye tracking based mutual gaze detection.
    /// </summary>
    private void UpdateEyeTracking()
    {
        // Detect if user is looking at agent's face
        float detectedLooking = DetectUserLookingAtAgent();

        // Smooth the value to avoid jitter
        userLookingAtMeValue = Mathf.Lerp(userLookingAtMeValue, detectedLooking,
            1f - smoothingFactor);

        // Detect if user is looking at any referent object
        float detectedLookingAtTarget = DetectUserLookingAtReferent();
        userLookingAtTargetValue = Mathf.Lerp(userLookingAtTargetValue, detectedLookingAtTarget,
            1f - smoothingFactor);

        // Update mutual gaze timer
        if (userLookingAtMeValue > 0.7f)
            mutualGazeTimer = Mathf.Clamp01(mutualGazeTimer + Time.deltaTime * 0.3f);
        else
            mutualGazeTimer = Mathf.Clamp01(mutualGazeTimer - Time.deltaTime * 0.5f);
    }

    /// <summary>
    /// Updates ASR turn-taking cues based on mic state and agent voice.
    /// </summary>
    private void UpdateASRTurnTaking()
    {
        wasMicActiveLastFrame = isMicActive;

        // Check if agent is currently speaking (prevents mic activation during agent turn)
        bool agentIsSpeaking = agentVoice != null && agentVoice.isPlaying;

        // Toggle mic with spacebar (only if agent is not speaking)
        if (Input.GetKeyDown(micToggleKey) && !agentIsSpeaking)
        {
            isMicActive = !isMicActive;

            if (isMicActive)
            {
                Debug.Log("ASR: Mic activated (user starting to speak)");
                timeMicHeld = 0f;
                timeSinceMicReleased = 0f;
            }
            else
            {
                Debug.Log("ASR: Mic deactivated (user finished speaking)");
                timeSinceMicReleased = 0f;
            }
        }

        // Force mic off if agent starts speaking
        if (agentIsSpeaking && isMicActive)
        {
            isMicActive = false;
            Debug.Log("ASR: Mic force-stopped (agent is speaking)");
        }

        // Update turn yield cue
        // HIGH when user JUST released mic (signaling they finished speaking = turn yield)
        if (!isMicActive && wasMicActiveLastFrame)
        {
            turnYieldValue = 1f; // Strong turn yield signal when mic released
        }
        else if (!isMicActive)
        {
            // Decay turn yield signal over time
            timeSinceMicReleased += Time.deltaTime;
            float decay = Mathf.Clamp01(1f - (timeSinceMicReleased / turnYieldDecayTime));
            turnYieldValue = decay;
        }
        else
        {
            // Mic is active, no turn yield
            turnYieldValue = 0f;
        }

        // Update user speaking cue
        // Consider user "speaking" after holding mic for a moment (avoids false positives)
        if (isMicActive)
        {
            timeMicHeld += Time.deltaTime;
            userSpeakingValue = timeMicHeld >= speakingStartDelay ? 1f : 0f;
        }
        else
        {
            timeMicHeld = 0f;
            userSpeakingValue = 0f;
        }
    }

    /// <summary>
    /// Detects if user's gaze fixation point is close to agent's face.
    /// Returns 1.0 if looking directly at face, 0.0 if looking away.
    /// </summary>
    private float DetectUserLookingAtAgent()
    {
        if (eyeTrackingExample == null ||
            eyeTrackingExample.fixationPointTransform == null ||
            agentFaceCenter == null)
            return 0f;

        // Get current gaze fixation point from eye tracking
        Vector3 gazePoint = eyeTrackingExample.fixationPointTransform.position;

        // Calculate distance from gaze point to agent's face center
        float distance = Vector3.Distance(gazePoint, agentFaceCenter.position);

        // Convert distance to probability [0,1]
        // Within threshold = 1.0 (looking at face)
        // Beyond threshold = 0.0 (looking away)
        float probability = Mathf.Clamp01(1f - (distance / gazeHitDistanceThreshold));

        return probability;
    }

    /// <summary>
    /// Detects if user is looking at any referent object.
    /// Checks against referents from the GazeDDMController.
    /// </summary>
    private float DetectUserLookingAtReferent()
    {
        if (eyeTrackingExample == null || eyeTrackingExample.fixationPointTransform == null)
            return 0f;

        // Get reference to GazeDDMController to access referent list
        GazeDDMController gazeController = GetComponent<GazeDDMController>();
        if (gazeController == null || gazeController.referents == null || gazeController.referents.Count == 0)
            return 0f;

        Vector3 gazePoint = eyeTrackingExample.fixationPointTransform.position;

        // Find closest referent
        float minDistance = float.MaxValue;
        foreach (var referent in gazeController.referents)
        {
            if (referent == null || referent.anchor == null) continue;

            float distance = Vector3.Distance(gazePoint, referent.anchor.position);
            if (distance < minDistance)
                minDistance = distance;
        }

        // Convert distance to probability
        float probability = Mathf.Clamp01(1f - (minDistance / referentGazeThreshold));

        return probability;
    }

    // ---------------------------
    // Interface Implementations
    // ---------------------------

    public float UserIsLookingAtMe()
    {
        return userLookingAtMeValue;
    }

    public float TurnYieldCue()
    {
        return turnYieldValue;
    }

    public float AffiliationGoal()
    {
        return simulatedAffiliationGoal;
    }

    public float AgentCognitiveLoad()
    {
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
        return simulatedDeixis;
    }

    public float BestReferentPriority()
    {
        return simulatedReferentPriority;
    }

    public float UserIsSpeaking()
    {
        return userSpeakingValue;
    }

    public float UserIsLookingAtTarget()
    {
        return userLookingAtTargetValue;
    }

    public float ComfortPrior()
    {
        return simulatedComfortPrior;
    }

    public float LongMutualGazeTimer()
    {
        return mutualGazeTimer;
    }

    // ---------------------------
    // Public API
    // ---------------------------

    /// <summary>
    /// Check if microphone is currently active (user is trying to speak).
    /// </summary>
    public bool IsMicActive()
    {
        return isMicActive;
    }

    /// <summary>
    /// Force stop the microphone (useful when agent needs to interrupt).
    /// </summary>
    public void StopMic()
    {
        if (isMicActive)
        {
            isMicActive = false;
            Debug.Log("ASR: Mic force-stopped externally");
        }
    }

    // ---------------------------
    // Debug Visualization
    // ---------------------------
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        // Simple debug display
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Box("Gaze Cues Debug");
        GUILayout.Label($"Mic Active: {isMicActive}");
        GUILayout.Label($"User Speaking: {userSpeakingValue:F2}");
        GUILayout.Label($"Turn Yield: {turnYieldValue:F2}");
        GUILayout.Label($"User Looks At Agent: {userLookingAtMeValue:F2}");
        GUILayout.Label($"User Looks At Target: {userLookingAtTargetValue:F2}");
        GUILayout.Label($"Mutual Gaze Timer: {mutualGazeTimer:F2}");
        GUILayout.Label($"\nPress [{micToggleKey}] to toggle mic");
        GUILayout.EndArea();
    }
}

