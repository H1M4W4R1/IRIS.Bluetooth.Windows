using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Addressing;
using IRIS.Communication;
using static IRIS.Bluetooth.Communication.Delegates;
using static IRIS.Communication.Delegates;

namespace IRIS.Bluetooth.Communication
{
    /// <summary>
    /// Base Interface for Bluetooth Low Energy communication
    /// </summary>
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

        public async ValueTask<bool> Connect(CancellationToken cancellationToken = default)
        {
            // Check if device is already connected
            if (IsConnected) return true;

            // Start scanning for devices
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Start();

            // Wait for connection
            while (!IsConnected)
            {
                if (cancellationToken.IsCancellationRequested) return false;
                await Task.Yield();
            }

            // Stop scanning for devices
            _watcher.Stop();
            _watcher.Received -= OnAdvertisementReceived;
            return true;
        }

        public ValueTask<bool> Disconnect(CancellationToken cancellationToken = default)
        {
            // Check if device is connected, if not - return
            if (!IsConnected) return ValueTask.FromResult(true);

            lock (ConnectedDevices)
            {
                // Remove device from connected devices
                ConnectedDevices.Remove(DeviceBluetoothAddress);
                DeviceBluetoothAddress = 0;

                // Disconnect from device if connected
                if (ConnectedDevice == null) return ValueTask.FromResult(true);

                // Send events
                BluetoothDeviceDisconnected(DeviceBluetoothAddress, ConnectedDevice);
                DeviceDisconnected?.Invoke(DeviceAddress);
                ConnectedDevice.Dispose();
                ConnectedDevice = null;
            }

            return ValueTask.FromResult(true);
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

        private async void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher watcher,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check if device is already connected
            if (IsConnected) return;

            // Check if device is already connected, if so - ignore
            // we don't need to lock this as it's a read-only operation
            if (ConnectedDevices.Contains(args.BluetoothAddress)) return;

            // Connect to device
            BluetoothLEDevice device =
                await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);

            // If the device is not found, ignore
            if (device == null) return;

            // Check if device matches expected address
            if (!await DeviceAddress.IsDeviceValid(device)) return;

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
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpoint(Guid serviceUUID, Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristic
            return await FindEndpoint(service, characteristicUUID);
        }

        /// <summary>
        /// Gets endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpoint(
            GattDeviceService? service,
            Guid characteristicUUID)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = await GetCharacteristic(service, characteristicUUID);

            // Check if characteristic is null
            if (characteristic == null) return null;

            // Return new endpoint
            return new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpoint(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristic
            return await FindEndpoint(service, characteristicIndex);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpoint(
            GattDeviceService? service,
            int characteristicIndex)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = await GetCharacteristic(service, characteristicIndex);

            // Check if characteristic is null
            if (characteristic == null) return null;

            // Return new endpoint
            return new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Get characteristic from service that has specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public async ValueTask<GattCharacteristic?> GetCharacteristic(Guid serviceUUID, Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get service
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristic
            return await GetCharacteristic(service, characteristicUUID);
        }

        public async ValueTask<GattCharacteristic?> GetCharacteristic(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get characteristic from service
            IReadOnlyList<GattCharacteristic>? characteristics =
                await GetAllCharacteristicsFor(service, characteristicUUID);

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
        public async ValueTask<GattCharacteristic?> GetCharacteristic(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristic
            return await GetCharacteristic(service, characteristicIndex);
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public async ValueTask<GattCharacteristic?> GetCharacteristic(
            GattDeviceService service,
            int characteristicIndex)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if index is negative
            if (characteristicIndex < 0) return null;

            // Get all characteristics
            IReadOnlyList<GattCharacteristic>? characteristics = await GetAllCharacteristics(service);

            // Check if characteristics are null
            if (characteristics == null) return null;

            // Check if index is out of bounds
            if (characteristicIndex >= characteristics.Count) return null;

            // Return characteristic
            return characteristics[characteristicIndex];
        }

        /// <summary>
        /// Get all characteristics from specified service (by UUID) that match specified characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>?> GetAllCharacteristicsFor(
            Guid serviceUUID,
            Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristics
            return await GetAllCharacteristicsFor(service, characteristicUUID);
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        public async ValueTask<IReadOnlyList<GattCharacteristic>?> GetAllCharacteristicsFor(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get specific characteristic
            GattCharacteristicsResult characteristic =
                await service.GetCharacteristicsForUuidAsync(characteristicUUID);

            // Check if characteristic is unreachable
            switch (characteristic.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await Disconnect();
                    return null;
                case GattCommunicationStatus.Success: break;
                default: return null;
            }

            // Return characteristic
            return characteristic.Characteristics;
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>?> GetAllCharacteristics(Guid serviceUUID)
        {
            GattDeviceService? service = await GetService(serviceUUID);

            // Check if service is null
            if (service == null) return null;

            // Get characteristics
            return await GetAllCharacteristics(service);
        }

        /// <summary>
        /// Get all characteristics from service
        /// </summary>
        /// <param name="service">Service to get characteristics from</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>?> GetAllCharacteristics(GattDeviceService service)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get characteristics
            GattCharacteristicsResult? characteristics = await service.GetCharacteristicsAsync();

            // Check if result is unreachable
            switch (characteristics.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await Disconnect();
                    return null;
                case GattCommunicationStatus.Success: break;
                default: return null;
            }

            // Return characteristics
            return characteristics.Characteristics;
        }

        /// <summary>
        /// Get service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>Service or null if not found</returns>
        public async ValueTask<GattDeviceService?> GetService(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if device exists
            if (ConnectedDevice == null) return null;

            // Get services
            IReadOnlyList<GattDeviceService>? services = await GetServices(serviceUUID);

            // Return first service found
            return services?.Count > 0 ? services[0] : null;
        }

        /// <summary>
        /// Get services from connected device
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of services</returns>
        public async ValueTask<IReadOnlyList<GattDeviceService>?> GetServices(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if device exists
            if (ConnectedDevice == null) return null;

            // Get service using UUID
            GattDeviceServicesResult? services = await ConnectedDevice.GetGattServicesForUuidAsync(serviceUUID);

            // Check if service is found
            switch (services.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await Disconnect();
                    return null;
                case GattCommunicationStatus.Success: break;
                default: return null;
            }

            // Return first service found
            return services.Services;
        }
    }
}