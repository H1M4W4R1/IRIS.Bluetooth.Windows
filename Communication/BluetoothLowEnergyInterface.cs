using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Addressing;
using IRIS.Communication;
using static IRIS.Bluetooth.Communication.Delegates;
using static IRIS.Communication.Delegates;

namespace IRIS.Bluetooth.Communication
{
    /// <summary>
    /// Base Interface for Bluetooth Low Energy communication
    /// </summary>
    // TODO: Synchronize async operations to thread this was called from
    public sealed class BluetoothLowEnergyInterface : ICommunicationInterface<IBluetoothLowEnergyAddress>
    {
        /// <summary>
        /// Address of current device
        /// </summary>
        private ulong DeviceBluetoothAddress { get; set; }

        /// <summary>
        /// Connected device
        /// </summary>
        private BluetoothLEDevice? ConnectedDevice { get; set; }

        /// <summary>
        /// List of all known connected device addresses
        /// used to connect to multiple devices
        /// </summary>
        private static List<ulong> ConnectedDevices { get; } = new();

        /// <summary>
        /// Service address to connect to
        /// </summary>
        public IBluetoothLowEnergyAddress DeviceAddress { get; init; }

        /// <summary>
        /// True if connected to device, false otherwise
        /// </summary>
        public bool IsConnected => ConnectedDevice != null || DeviceBluetoothAddress != 0;

        /// <summary>
        /// Get name of the connected device
        /// </summary>
        public string DeviceName => ConnectedDevice?.Name ?? "None";

        /// <summary>
        /// Device watcher for scanning for devices
        /// </summary>
        private readonly BluetoothLEAdvertisementWatcher _watcher;

        public event DeviceConnectedHandler<IBluetoothLowEnergyAddress>? DeviceConnected;
        public event DeviceDisconnectedHandler<IBluetoothLowEnergyAddress>? DeviceDisconnected;
        public event DeviceConnectionLostHandler<IBluetoothLowEnergyAddress>? DeviceConnectionLost;
        public event BluetoothDeviceConnectedHandler BluetoothDeviceConnected = delegate { };
        public event BluetoothDeviceDisconnectedHandler BluetoothDeviceDisconnected = delegate { };

        public ValueTask<bool> Connect(CancellationToken cancellationToken = default)
        {
            // Check if device is already connected
            if (IsConnected) return new ValueTask<bool>(true);

            // Start scanning for devices
            try
            {
                _watcher.Received += OnAdvertisementReceived;
                _watcher.Start();

                // Wait for connection
                while (!IsConnected)
                {
                    if (cancellationToken.IsCancellationRequested) return new ValueTask<bool>(false);
                }

                // Stop scanning for devices
                _watcher.Stop();
                _watcher.Received -= OnAdvertisementReceived;
                return new ValueTask<bool>(true);
            }
            catch (COMException) // When adapter is not available, hope no other exceptions are allocated here
            {
                _watcher.Received -= OnAdvertisementReceived;
                return new ValueTask<bool>(false);
            }
        }

        public ValueTask<bool> Disconnect()
        {
            // Check if device is connected, if not - return
            if (!IsConnected) return new ValueTask<bool>(true);

            lock (ConnectedDevices)
            {
                // Remove device from connected devices
                ConnectedDevices.Remove(DeviceBluetoothAddress);
                DeviceBluetoothAddress = 0;

                // Disconnect from device if connected
                if (ConnectedDevice == null) return new ValueTask<bool>(true);

                // Send events
                BluetoothDeviceDisconnected(DeviceBluetoothAddress, ConnectedDevice);
                DeviceDisconnected?.Invoke(DeviceAddress);
                ConnectedDevice.Dispose();
                ConnectedDevice = null;
            }

            return new ValueTask<bool>(true);
        }

        /// <summary>
        /// Create Bluetooth Low Energy interface for given device name (use regex to match wildcards)
        /// </summary>
        public BluetoothLowEnergyInterface(string deviceNameRegex)
        {
            // Create new service address
            DeviceAddress = new BluetoothLowEnergyNameAddress(deviceNameRegex);

            // Create new watcher for service address
            _watcher = new BluetoothLEAdvertisementWatcher()
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                AdvertisementFilter = DeviceAddress.GetAdvertisementFilter(),
                SignalStrengthFilter = DeviceAddress.GetSignalStrengthFilter()
            };
        }

        /// <summary>
        /// Create Bluetooth Low Energy interface for given service addresses
        /// </summary>
        public BluetoothLowEnergyInterface(Guid serviceAddress)
        {
            // Create new service address
            DeviceAddress = new BluetoothLowEnergyServiceAddress(serviceAddress);

            // Create new watcher for service address
            _watcher = new BluetoothLEAdvertisementWatcher()
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                AdvertisementFilter = DeviceAddress.GetAdvertisementFilter(),
                SignalStrengthFilter = DeviceAddress.GetSignalStrengthFilter()
            };
        }

        public BluetoothLowEnergyInterface(ulong deviceBluetoothAddress)
        {
            // Create new service address
            DeviceAddress = new BluetoothLowEnergyDeviceIdentifierAddress(deviceBluetoothAddress);

            // Create new watcher for service address
            _watcher = new BluetoothLEAdvertisementWatcher()
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                AdvertisementFilter = DeviceAddress.GetAdvertisementFilter(),
                SignalStrengthFilter = DeviceAddress.GetSignalStrengthFilter()
            };
        }

        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher watcher,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check if device is already connected
            if (IsConnected) return;

            // Check if device is already connected, if so - ignore
            // we don't need to lock this as it's a read-only operation
            if (ConnectedDevices.Contains(args.BluetoothAddress)) return;

            // Connect to device
            IAsyncOperation<BluetoothLEDevice> deviceOperation = BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            
            // Wait for device to connect
            while (deviceOperation.Status != AsyncStatus.Completed)
            {
                // Check if operation was cancelled or errored
                if (deviceOperation.Status is not (AsyncStatus.Canceled or AsyncStatus.Error)) continue;
                return;
            }
            
            // Get device
            BluetoothLEDevice? device = deviceOperation.GetResults();
            
            // Check if device is null
            if (device == null) return;

            // Check if device matches expected address
            if (!DeviceAddress.IsDeviceValid(device)) return;

            lock (ConnectedDevices)
            {
                // Additional check just in case nothing went wrong in meanwhile
                if (IsConnected) return;

                // Add device to connected devices
                ConnectedDevices.Add(args.BluetoothAddress);
                DeviceBluetoothAddress = args.BluetoothAddress;
                ConnectedDevice = device;
                BluetoothDeviceConnected(DeviceBluetoothAddress, device);
                DeviceConnected?.Invoke(DeviceAddress);
            }
        }

        /// <summary>
        /// Gets endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>Endpoint or null if not found</returns>
        public BluetoothLowEnergyEndpoint? FindEndpoint(Guid serviceUUID, Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = GetService(serviceUUID);
            
            // Check if service is null and return
            return service == null ? null : FindEndpoint(service, characteristicUUID);
        }

        /// <summary>
        /// Gets endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public BluetoothLowEnergyEndpoint? FindEndpoint(
            GattDeviceService? service,
            Guid characteristicUUID)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = GetCharacteristic(service, characteristicUUID);
            
            // Check if characteristic is null and return
            return characteristic == null ? null : new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public BluetoothLowEnergyEndpoint? FindEndpoint(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = GetService(serviceUUID);

            // Check if service is null and return
            return service == null ? null : FindEndpoint(service, characteristicIndex);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public BluetoothLowEnergyEndpoint? FindEndpoint(
            GattDeviceService? service,
            int characteristicIndex)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = GetCharacteristic(service, characteristicIndex);

            // Check if characteristic is null and return
            return characteristic == null ? null : new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Get characteristic from service that has specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public GattCharacteristic? GetCharacteristic(Guid serviceUUID, Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get service
            GattDeviceService? service = GetService(serviceUUID);

            // Check if service is null and return
            return service == null ? null : GetCharacteristic(service, characteristicUUID);
        }

        public GattCharacteristic? GetCharacteristic(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get characteristic from service
            IReadOnlyList<GattCharacteristic>? characteristics =
                GetAllCharacteristicsFor(service, characteristicUUID);

            // Check if characteristics are null
            if (characteristics == null) return null;

            // Return first characteristic found
            return characteristics.Count > 0 ? characteristics[0] : null;
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public GattCharacteristic? GetCharacteristic(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = GetService(serviceUUID);

            // Check if service is null and return
            return service == null ? null : GetCharacteristic(service, characteristicIndex);
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public GattCharacteristic? GetCharacteristic(
            GattDeviceService service,
            int characteristicIndex)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if index is negative
            if (characteristicIndex < 0) return null;

            // Get all characteristics
            IReadOnlyList<GattCharacteristic>? characteristics = GetAllCharacteristics(service);

            // Check if characteristics are null
            if (characteristics == null) return null;

            // Check if index is out of bounds
            if (characteristicIndex >= characteristics.Count)
                return null;

            // Get characteristic
            return characteristics[characteristicIndex];
        }

        /// <summary>
        /// Get all characteristics from specified service (by UUID) that match specified characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public IReadOnlyList<GattCharacteristic>? GetAllCharacteristicsFor(
            Guid serviceUUID,
            Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = GetService(serviceUUID);

            // Check if service is null and return
            return service == null ? null : GetAllCharacteristicsFor(service, characteristicUUID);
        }

        /// <summary>
        /// Get all characteristics from specified service that match specified characteristic UUID
        /// </summary>
        /// <param name="service">Service to get characteristics from</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public IReadOnlyList<GattCharacteristic>? GetAllCharacteristicsFor(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get all characteristics
            IReadOnlyList<GattCharacteristic>? characteristics = GetAllCharacteristics(service);

            // Check if characteristics are null
            if (characteristics == null) return null;

            // Filter characteristics by UUID
            List<GattCharacteristic> filteredCharacteristics = new();
            foreach (GattCharacteristic characteristic in characteristics)
            {
                if (characteristic.Uuid == characteristicUUID)
                    filteredCharacteristics.Add(characteristic);
            }

            // Return filtered characteristics
            return filteredCharacteristics.Count > 0 ? filteredCharacteristics : null;
        }

        /// <summary>
        /// Get all characteristics from specified service (by UUID)
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public IReadOnlyList<GattCharacteristic>? GetAllCharacteristics(Guid serviceUUID)
        {
            // Get service
            GattDeviceService? service = GetService(serviceUUID);

            // Check if service is null and return
            return service == null ? null : GetAllCharacteristics(service);
        }

        /// <summary>
        /// Get all characteristics from specified service
        /// </summary>
        /// <param name="service">Service to get characteristics from</param>
        /// <returns>List of characteristics or null if not found</returns>
        public IReadOnlyList<GattCharacteristic>? GetAllCharacteristics(GattDeviceService service)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get all characteristics
            GattCharacteristicsResult characteristics = service.GetCharacteristicsAsync().GetResults();

            // Check if characteristics are null
            if (characteristics == null) return null;

            // Check if characteristics status is success
            if (characteristics.Status != GattCommunicationStatus.Success)
                return null;

            // Return characteristics
            return characteristics.Characteristics;
        }

        /// <summary>
        /// Get service from device that has specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>Service or null if not found</returns>
        public GattDeviceService? GetService(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if connected device is null
            if (ConnectedDevice == null) return null;

            // Get services
            IReadOnlyList<GattDeviceService>? services = GetServices(serviceUUID);

            // Check if services are null
            if (services == null) return null;

            // Return first service found
            return services.Count > 0 ? services[0] : null;
        }

        /// <summary>
        /// Get all services from device that match specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of services or null if not found</returns>
        public IReadOnlyList<GattDeviceService>? GetServices(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if connected device is null
            if (ConnectedDevice == null) return null;

            // Get all services
            GattDeviceServicesResult servicesResult = ConnectedDevice.GetGattServicesAsync().GetResults();

            // Check if services are null
            if (servicesResult == null) return null;

            // Check if services status is success
            if (servicesResult.Status != GattCommunicationStatus.Success)
                return null;

            // Filter services by UUID
            List<GattDeviceService> filteredServices = new();
            foreach (GattDeviceService service in servicesResult.Services)
            {
                if (service.Uuid == serviceUUID)
                    filteredServices.Add(service);
            }

            // Return filtered services
            return filteredServices.Count > 0 ? filteredServices : null;
        }
    }
}