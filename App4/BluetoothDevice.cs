using Windows.Devices.Bluetooth;

public class BluetoothDevice
{
    public string DeviceName { get; set; }
    public string DeviceId { get; set; }
    public BluetoothLEDevice DeviceReference { get; set; }
    public bool IsPaired { get; set; }  // Added property for pairing status
}
