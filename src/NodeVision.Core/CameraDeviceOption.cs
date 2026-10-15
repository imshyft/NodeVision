using System.Collections.Generic;
using System.Linq;

namespace NodeVision.Core;

public sealed record CameraDeviceOption(
    int DeviceIndex,
    string DisplayName,
    IReadOnlyList<CameraMode> Modes)
{
    /// <summary>
    /// Highest frame rate wins (least motion blur), then the largest frame, because a camera's
    /// top frame rate is often only offered at a lower resolution.
    /// </summary>
    public CameraMode? DefaultMode =>
        Modes.Count == 0
            ? null
            : Modes
                .OrderByDescending(mode => mode.Fps)
                .ThenByDescending(mode => (long)mode.Width * mode.Height)
                .First();

    public override string ToString()
    {
        return DisplayName;
    }
}