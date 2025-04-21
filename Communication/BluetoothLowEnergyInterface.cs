using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Addressing;
using IRIS.Communication.Abstract;
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

        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            // Check if device is already connected
            if (IsConnected) return ValueTask.FromResult(true);

            // Start scanning for devices
            try
            {
                _watcher.Received += OnAdvertisementReceivedAsync;
                _watcher.Start();

                // Wait for connection
                while (!IsConnected)
                {
                    if (cancellationToken.IsCancellationRequested) return ValueTask.FromResult(false);
                }

                // Stop scanning for devices
                _watcher.Stop();
                _watcher.Received -= OnAdvertisementReceivedAsync;
                return ValueTask.FromResult(true);
            }
            catch (COMException) // When adapter is not available, hope no other exceptions are allocated here
            {
                _watcher.Received -= OnAdvertisementReceivedAsync;
                return ValueTask.FromResult(false);
            }
        }

        public ValueTask<bool> DisconnectAsync()
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

        private async void OnAdvertisementReceivedAsync(
            BluetoothLEAdvertisementWatcher watcher,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check if device is already connected
            if (IsConnected) return;

            // Check if device is already connected, if so - ignore
            // we don't need to lock this as it's a read-only operation
            if (ConnectedDevices.Contains(args.BluetoothAddress)) return;

            // Connect to device
            BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);


            // Check if device is null
            if (device == null) return;

            // Check if device matches expected address
            if (!await DeviceAddress.IsDeviceValidAsync(device)) return;

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
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpointAsync(Guid serviceUUID, Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null and return
            if (service == null) return null;
            
            // Return endpoint from service and characteristic UUID
            return await FindEndpointAsync(service, characteristicUUID);
        }

        /// <summary>
        /// Gets endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpointAsync(
            GattDeviceService? service,
            Guid characteristicUUID)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = await GetCharacteristicAsync(service, characteristicUUID);

            // Check if characteristic is null and return endpoint if not
            return characteristic == null ? null : new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpointAsync(
            Guid serviceUUID,
            int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null and return
            if (service == null) return null;

            // Return endpoint from service and characteristic index
            return await FindEndpointAsync(service, characteristicIndex);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public async ValueTask<BluetoothLowEnergyEndpoint?> FindEndpointAsync(
            GattDeviceService? service,
            int characteristicIndex)
        {
            // Get service
            if (service == null) return null;

            // Get characteristic
            GattCharacteristic? characteristic = await GetCharacteristicAsync(service, characteristicIndex);

            // Check if characteristic is null and return endpoint if not
            return characteristic == null ? null : new BluetoothLowEnergyEndpoint(this, service, characteristic);
        }

        /// <summary>
        /// Get characteristic from service that has specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public async ValueTask<GattCharacteristic?> GetCharacteristicAsync(Guid serviceUUID, Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get service
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null and return
            if (service == null) return null;

            // Return characteristic
            return await GetCharacteristicAsync(service, characteristicUUID);
        }

        public async ValueTask<GattCharacteristic?> GetCharacteristicAsync(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Get characteristic from service
            IReadOnlyList<GattCharacteristic> characteristics =
                await GetAllCharacteristicsForAsync(service, characteristicUUID);

            // Check if list is empty and return first characteristic found if not
            return characteristics.Count == 0 ? null : characteristics[0];
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public async ValueTask<GattCharacteristic?> GetCharacteristicAsync(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null and return
            if (service == null) return null;

            // Return characteristic
            return await GetCharacteristicAsync(service, characteristicIndex);
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        /// <exception cref="ArgumentOutOfRangeException">If index is negative</exception>
        public async ValueTask<GattCharacteristic?> GetCharacteristicAsync(
            GattDeviceService service,
            int characteristicIndex)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if index is negative
            if (characteristicIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(characteristicIndex), "Index cannot be negative");

            // Get all characteristics
            IReadOnlyList<GattCharacteristic> characteristics = await GetAllCharacteristicsAsync(service);

            // Check if index is out of bounds, also automatically verifies if index is
            // in valid range
            return characteristicIndex >= characteristics.Count ? null : characteristics[characteristicIndex];
        }

        /// <summary>
        /// Get all characteristics from specified service (by UUID) that match specified characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsForAsync(
            Guid serviceUUID,
            Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null and return
            if (service == null) return [];

            // Get all characteristics
            return await GetAllCharacteristicsForAsync(service, characteristicUUID);
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        public async ValueTask<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsForAsync(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return [];

            // Get specific characteristic
            GattCharacteristicsResult characteristics =
                await service.GetCharacteristicsForUuidAsync(characteristicUUID);

            // Check if characteristic is unreachable
            switch (characteristics.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await DisconnectAsync();
                    return [];
                case GattCommunicationStatus.Success: break;
                default: return [];
            }

            // Return characteristic
            return characteristics.Characteristics;
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsAsync(Guid serviceUUID)
        {
            GattDeviceService? service = await GetServiceAsync(serviceUUID);

            // Check if service is null
            if (service == null) return [];

            // Get all characteristics
            return await GetAllCharacteristicsAsync(service);
        }

        /// <summary>
        /// Get all characteristics from service
        /// </summary>
        /// <param name="service">Service to get characteristics from</param>
        /// <returns>List of characteristics or null if not found</returns>
        public async ValueTask<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsAsync(
            GattDeviceService service)
        {
            // Check if device is connected
            if (!IsConnected) return [];

            // Get characteristics
            GattCharacteristicsResult? characteristics = await service.GetCharacteristicsAsync();

            // Check if characteristics are null
            if (characteristics == null) return [];

            // Check if result is unreachable
            switch (characteristics.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await DisconnectAsync();
                    return [];
                case GattCommunicationStatus.Success: break;
                default: return [];
            }

            // Return characteristics
            return characteristics.Characteristics;
        }

        /// <summary>
        /// Get service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>Service or null if not found</returns>
        public async ValueTask<GattDeviceService?> GetServiceAsync(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return null;

            // Check if device exists
            if (ConnectedDevice == null) return null;

            // Get services
            IReadOnlyList<GattDeviceService> services = await GetServicesAsync(serviceUUID);

            // Check if services are empty 
            return services.Count == 0 ? null : services[0];
        }

        /// <summary>
        /// Get services from connected device
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of services</returns>
        public async ValueTask<IReadOnlyList<GattDeviceService>> GetServicesAsync(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return [];

            // Check if device exists
            if (ConnectedDevice == null) return [];

            // Get service using UUID
            GattDeviceServicesResult servicesResult =
                await ConnectedDevice.GetGattServicesForUuidAsync(serviceUUID);

            // Check if service is found
            switch (servicesResult.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    await DisconnectAsync();
                    return [];
                case GattCommunicationStatus.Success: break;
                default: return [];
            }

            // Return first service found
            return servicesResult.Services;
        }
    }
}