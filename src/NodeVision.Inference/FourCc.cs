namespace NodeVision.Inference;

/// <summary>
/// Converts between four-character pixel-format codes and their readable form, for the values
/// carried on <see cref="Core.CameraMode.PixelFormat"/>.
/// </summary>
internal static class FourCc
{
    public static bool TryPack(string format, out int fourCc)
    {
        fourCc = 0;
        if (format.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
            fourCc |= format[i] << (i * 8);

        return true;
    }

    public static string ToString(int fourCc)
    {
        if (fourCc == 0)
            return "RGB";

        var chars = new char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((fourCc >> (i * 8)) & 0xFF);
            if (c is < ' ' or > '~')
                return $"0x{fourCc:X8}";
            chars[i] = c;
        }

        return new string(chars);
    }
}
