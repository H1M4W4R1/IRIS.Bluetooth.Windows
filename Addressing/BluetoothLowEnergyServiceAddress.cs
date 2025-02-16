using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;

namespace IRIS.Bluetooth.Addressing
{
    /// <summary>
    /// Represents a Bluetooth LE service address
    /// Can also match device name using regular expression
    /// </summary>
    public readonly struct BluetoothLowEnergyServiceAddress : IBluetoothLowEnergyAddress
    {
        private readonly BluetoothLEAdvertisementFilter _cachedAdvertisementFilter;
        private readonly BluetoothSignalStrengthFilter _cachedSignalStrengthFilter;

        /// <summary>
        /// Regular expression to match device name
        /// </summary>
        public string? NameRegex { get; init; }

        /// <summary>
        /// UUID of the service
        /// </summary>
        public Guid ServiceUUID { get; init; }

        /// <summary>
        /// Get the advertisement filter for this service
        /// </summary>
        public BluetoothLEAdvertisementFilter GetAdvertisementFilter() => _cachedAdvertisementFilter;

        /// <summary>
        /// Get the signal strength filter for this service
        /// </summary>
        public BluetoothSignalStrengthFilter GetSignalStrengthFilter() => _cachedSignalStrengthFilter;

        /// <summary>
        /// Check if the device is valid for this service
        /// </summary>
        public bool IsDeviceValid(BluetoothLEDevice device)
        {
            // Cache service UUID because C#...
            Guid uuid = ServiceUUID;

            // Get service
            IAsyncOperation<GattDeviceServicesResult> serviceResult = device.GetGattServicesAsync();

            // Wait for result
            while (serviceResult.Status != AsyncStatus.Completed)
            {
                // Check if the task was cancelled
                if (serviceResult.Status == AsyncStatus.Canceled || serviceResult.Status == AsyncStatus.Error)
                    return false;
            }
            
            // Get result status
            if (serviceResult.GetResults() is not { } services) return false;

            // Ensure communication status is OK
            if (services.Status != GattCommunicationStatus.Success) return false;

            // Get service
            GattDeviceService? service =
                services.Services.FirstOrDefault(s => s.Uuid == uuid);

            // Check if device has the service
            return service != null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public BluetoothLowEnergyServiceAddress(
            Guid serviceUUID,
            short minSignalStrength = -75,
            short maxSignalStrength = -70,
            uint timeoutSeconds = 2)
        {
            // Copy service UUIDs
            ServiceUUID = serviceUUID;

            // Create advertisement filter
            _cachedAdvertisementFilter = new BluetoothLEAdvertisementFilter
            {
                Advertisement = new BluetoothLEAdvertisement
                {
                    ServiceUuids =
                    {
                        ServiceUUID
                    }
                }
            };

            // Create signal filter
            _cachedSignalStrengthFilter = new BluetoothSignalStrengthFilter
            {
                InRangeThresholdInDBm = maxSignalStrength,
                OutOfRangeThresholdInDBm = minSignalStrength,
                OutOfRangeTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
        }

        public override string ToString() => $"{ServiceUUID}";
    }
}