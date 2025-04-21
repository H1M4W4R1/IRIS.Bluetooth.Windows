using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using IRIS.Utility;

namespace IRIS.Bluetooth.Addressing
{
    /// <summary>
    ///     Bluetooth device address
    /// </summary>
    public interface IBluetoothLowEnergyAddress
    {
        /// <summary>
        ///     Get the advertisement filter for this address
        /// </summary>
        public BluetoothLEAdvertisementFilter GetAdvertisementFilter();
        
        /// <summary>
        ///     Get the signal strength filter for this address
        /// </summary>
        public BluetoothSignalStrengthFilter GetSignalStrengthFilter();

        /// <summary>
        ///     Check if the device is valid for this address
        /// </summary>
        /// <param name="device">Device to check</param>
        /// <returns>True if the device is valid</returns>
        public bool IsDeviceValid(BluetoothLEDevice device) =>
            IsDeviceValidAsync(device).Wait();
        
        /// <summary>
        ///     Check if the device is valid for this address
        /// </summary>
        public ValueTask<bool> IsDeviceValidAsync(BluetoothLEDevice device);
    }
}