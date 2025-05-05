using UnityEngine;

[System.Serializable]
public struct EnvironmentSettings
{
    public bool enabled;
    public Color groundColour;
    public Color skyColourHorizon;
    public Color skyColourZenith;
    public Light sunLight;
    public float sunFocus;
    public float sunIntensity;
}