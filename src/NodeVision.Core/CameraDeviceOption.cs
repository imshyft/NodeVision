namespace NodeVision.Core;

public sealed record CameraDeviceOption(
    int DeviceIndex,
    string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}