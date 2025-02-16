using Windows.Devices.Bluetooth;

namespace IRIS.Bluetooth.Communication
{
    public static class Delegates
    {
        /// <summary>
        /// Called when device connection is lost
        /// </summary>
        public delegate void BluetoothDeviceDisconnectedHandler(ulong address, BluetoothLEDevice device);

        /// <summary>
        /// Called when a device is connected
        /// </summary>
        public delegate void BluetoothDeviceConnectedHandler(ulong address, BluetoothLEDevice device);
    }
}