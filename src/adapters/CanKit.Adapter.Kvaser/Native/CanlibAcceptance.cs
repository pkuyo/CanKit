namespace CanKit.Adapter.Kvaser.Native;

/// <summary>
/// Acceptance filter sequence shared by the real CANlib binding and the FAKE double, so the
/// FAKE test suite exercises the same code/mask/flag logic that runs against hardware.
/// </summary>
internal static class CanlibAcceptance
{
    /// <summary>
    /// Programs code and mask of the standard (11-bit) or the extended (29-bit) filter.
    /// CANlib accepts a frame when <c>(code XOR id) AND mask == 0</c>.
    /// </summary>
    public static Canlib.canStatus Set(int hnd, uint code, uint mask, bool extended)
    {
        var status = Canlib.canAccept(hnd, unchecked((int)code),
            extended ? Canlib.canFILTER_SET_CODE_EXT : Canlib.canFILTER_SET_CODE_STD);
        if (status != Canlib.canStatus.canOK)
            return status;

        return Canlib.canAccept(hnd, unchecked((int)mask),
            extended ? Canlib.canFILTER_SET_MASK_EXT : Canlib.canFILTER_SET_MASK_STD);
    }
}
