using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace IRIS.Bluetooth.Addressing
{
    public readonly struct BluetoothLowEnergyNameAddress : IBluetoothLowEnergyAddress
    {
        private readonly BluetoothLEAdvertisementFilter _cachedAdvertisementFilter;
        private readonly BluetoothSignalStrengthFilter _cachedSignalStrengthFilter;
        
        /// <summary>
        /// Regular expression to match device name
        /// </summary>
        public string NameRegex { get; init; }
        
        public BluetoothLEAdvertisementFilter GetAdvertisementFilter() => _cachedAdvertisementFilter;
        public BluetoothSignalStrengthFilter GetSignalStrengthFilter() => _cachedSignalStrengthFilter;
        
        /// <summary>
        /// Check if the device is valid for this address
        /// </summary>
        public ValueTask<bool> IsDeviceValid(BluetoothLEDevice device)
        {
            return ValueTask.FromResult(device.Name is { } name 
                                        && NameRegex is { } regex
                                        && Regex.IsMatch(name, regex));
        }

        public BluetoothLowEnergyNameAddress(string deviceNameRegex)
        {
            NameRegex = deviceNameRegex;
            
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

        public override string ToString() => $"{NameRegex}";
    }
}