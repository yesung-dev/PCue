namespace PCue.Models;

public enum CueEndAction
{
    /// <summary>Follows the playlist-wide "자동 다음" toggle.</summary>
    Default,
    Next,
    Stop,
    /// <summary>Video: freeze on the last frame. Audio behaves like Stop.</summary>
    Hold,
    Loop
}
