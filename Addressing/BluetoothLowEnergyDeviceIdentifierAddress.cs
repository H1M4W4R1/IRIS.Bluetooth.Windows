using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace IRIS.Bluetooth.Addressing
{
    public readonly struct BluetoothLowEnergyDeviceIdentifierAddress : IBluetoothLowEnergyAddress
    {
        private readonly BluetoothLEAdvertisementFilter _cachedAdvertisementFilter;
        private readonly BluetoothSignalStrengthFilter _cachedSignalStrengthFilter;

        /// <summary>
        /// Address of the device
        /// </summary>
        public ulong DeviceAddress { get; private init; }

        public BluetoothLEAdvertisementFilter GetAdvertisementFilter() => _cachedAdvertisementFilter;
        public BluetoothSignalStrengthFilter GetSignalStrengthFilter() => _cachedSignalStrengthFilter;

        /// <summary>
        /// Check if the device is valid for this address
        /// </summary>
        public bool IsDeviceValid(BluetoothLEDevice device) => device.BluetoothAddress == DeviceAddress;

        public BluetoothLowEnergyDeviceIdentifierAddress(ulong deviceAddress)
        {
            DeviceAddress = deviceAddress;

            // Create advertisement filter
            _cachedAdvertisementFilter = new BluetoothLEAdvertisementFilter
            {
                // We are looking for any advertisement
                Advertisement = new BluetoothLEAdvertisement()
            };

            _cachedSignalStrengthFilter = new BluetoothSignalStrengthFilter
            {
                InRangeThresholdInDBm = -75,
                OutOfRangeThresholdInDBm = -70,
                OutOfRangeTimeout = TimeSpan.FromSeconds(2)
            };
        }

        public override string ToString() => $"{DeviceAddress:X}";
    }
}