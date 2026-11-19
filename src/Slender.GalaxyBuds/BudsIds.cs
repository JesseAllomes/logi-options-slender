namespace Slender.GalaxyBuds;

/// <summary>Message ids of the buds control channel that Slender reads or sends (everything else is skipped, never sent).</summary>
public static class BudsIds
{
    public const byte DebugSku = 34;
    public const byte Ack = 66;
    public const byte StatusUpdated = 96;
    public const byte ExtendedStatusUpdated = 97;
    public const byte VersionInfoLong = 104;
    public const byte NoiseControlsUpdate = 119;

    // Read requests (empty payload); the buds answer with the same id.
    public const byte MultipointInfo = 118;
    public const byte BatteryCycles = 206;

    public const byte AutoPause = 108;
    public const byte InEarCalls = 110;
    public const byte NoiseOneBud = 111;
    public const byte NoiseControls = 120;
    public const byte Cycle = 121;
    public const byte VoiceDetect = 122;
    public const byte VoiceDetectTimeout = 123;
    public const byte SpatialAudio = 124;
    public const byte AmbientCustom = 130;
    public const byte AmbientLevel = 132;
    public const byte EqPreset = 134;
    public const byte Sidetone = 139;
    public const byte Balance = 143;
    public const byte TouchHold = 146;
    public const byte FindStart = 160;
    public const byte FindStop = 161;
    public const byte Seamless = 175;
    public const byte AdaptiveEq = 218;
    public const byte SirenDetect = 222;
}
