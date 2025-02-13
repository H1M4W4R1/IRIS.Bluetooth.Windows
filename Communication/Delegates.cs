using Windows.Devices.Bluetooth;

namespace IRIS.Bluetooth.Communication
{
    /// <summary>
    /// Called when device connection is lost
    /// </summary>
    public delegate void DeviceDisconnected(ulong address, BluetoothLEDevice device);

    /// <summary>
    /// Called when a device is connected
    /// </summary>
    public delegate void DeviceConnected(ulong address, BluetoothLEDevice device);
}