using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Addressing;
using IRIS.Communication;
using IRIS.Data;
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

        public bool Connect(CancellationToken cancellationToken = default)
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
            }

            // Stop scanning for devices
            _watcher.Stop();
            _watcher.Received -= OnAdvertisementReceived;
            return true;
        }

        public bool Disconnect()
        {
            // Check if device is connected, if not - return
            if (!IsConnected) return true;

            lock (ConnectedDevices)
            {
                // Remove device from connected devices
                ConnectedDevices.Remove(DeviceBluetoothAddress);
                DeviceBluetoothAddress = 0;

                // Disconnect from device if connected
                if (ConnectedDevice == null) return true;

                // Send events
                BluetoothDeviceDisconnected(DeviceBluetoothAddress, ConnectedDevice);
                DeviceDisconnected?.Invoke(DeviceAddress);
                ConnectedDevice.Dispose();
                ConnectedDevice = null;
            }

            return true;
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
        public DataPromise<BluetoothLowEnergyEndpoint> FindEndpoint(Guid serviceUUID, Guid characteristicUUID)
        {
            // Get service
            DataPromise<GattDeviceService> service = GetService(serviceUUID);
            
            // Check if service is null and return
            return !service.HasData
                ? DataPromise<BluetoothLowEnergyEndpoint>.FromFailure()
                : FindEndpoint(service.Data, characteristicUUID);
        }

        /// <summary>
        /// Gets endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public DataPromise<BluetoothLowEnergyEndpoint> FindEndpoint(
            GattDeviceService? service,
            Guid characteristicUUID)
        {
            // Get service
            if (service == null) return DataPromise<BluetoothLowEnergyEndpoint>.FromFailure();

            // Get characteristic
            DataPromise<GattCharacteristic> characteristic = GetCharacteristic(service, characteristicUUID);
            
            // Check if characteristic is null and return
            return !characteristic.HasData
                ? DataPromise<BluetoothLowEnergyEndpoint>.FromFailure()
                : DataPromise.FromSuccess(new BluetoothLowEnergyEndpoint(this, service, characteristic.Data));
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public DataPromise<BluetoothLowEnergyEndpoint> FindEndpoint(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            DataPromise<GattDeviceService> service = GetService(serviceUUID);

            // Check if service is null and return
            return !service.HasData
                ? DataPromise<BluetoothLowEnergyEndpoint>.FromFailure()
                : FindEndpoint(service.Data, characteristicIndex);
        }

        /// <summary>
        /// Finds endpoint for specified service and characteristic index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Endpoint or null if not found</returns>
        public DataPromise<BluetoothLowEnergyEndpoint> FindEndpoint(
            GattDeviceService? service,
            int characteristicIndex)
        {
            // Get service
            if (service == null) return DataPromise<BluetoothLowEnergyEndpoint>.FromFailure();

            // Get characteristic
            DataPromise<GattCharacteristic> characteristic = GetCharacteristic(service, characteristicIndex);

            // Check if characteristic is null and return
            return !characteristic.HasData
                ? DataPromise<BluetoothLowEnergyEndpoint>.FromFailure()
                : DataPromise.FromSuccess(new BluetoothLowEnergyEndpoint(this, service, characteristic.Data));
        }

        /// <summary>
        /// Get characteristic from service that has specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicUUID">UUID of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public DataPromise<GattCharacteristic> GetCharacteristic(Guid serviceUUID, Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<GattCharacteristic>.FromFailure();

            // Get service
            DataPromise<GattDeviceService> service = GetService(serviceUUID);

            // Check if service is null and return
            return !service.HasData
                ? DataPromise<GattCharacteristic>.FromFailure()
                : GetCharacteristic(service.Data, characteristicUUID);
        }

        public DataPromise<GattCharacteristic> GetCharacteristic(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<GattCharacteristic>.FromFailure();

            // Get characteristic from service
            DataPromise<IReadOnlyList<GattCharacteristic>> characteristics =
                GetAllCharacteristicsFor(service, characteristicUUID);

            // Check if characteristics are null
            if (!characteristics.HasData) return DataPromise<GattCharacteristic>.FromFailure();

            // Return first characteristic found
            return characteristics.Data.Count > 0
                ? DataPromise.FromSuccess(characteristics.Data[0])
                : DataPromise<GattCharacteristic>.FromFailure();
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public DataPromise<GattCharacteristic> GetCharacteristic(Guid serviceUUID, int characteristicIndex)
        {
            // Get service
            DataPromise<GattDeviceService> service = GetService(serviceUUID);

            // Check if service is null and return
            return !service.HasData
                ? DataPromise<GattCharacteristic>.FromFailure()
                : GetCharacteristic(service.Data, characteristicIndex);
        }

        /// <summary>
        /// Get characteristic from service that is at specified index
        /// </summary>
        /// <param name="service">Service to get characteristic from</param>
        /// <param name="characteristicIndex">Index of the characteristic</param>
        /// <returns>Characteristic or null if not found</returns>
        public DataPromise<GattCharacteristic> GetCharacteristic(
            GattDeviceService service,
            int characteristicIndex)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<GattCharacteristic>.FromFailure();

            // Check if index is negative
            if (characteristicIndex < 0) return DataPromise<GattCharacteristic>.FromFailure();

            // Get all characteristics
            DataPromise<IReadOnlyList<GattCharacteristic>> characteristics = GetAllCharacteristics(service);

            // Check if characteristics are null
            if (!characteristics.HasData) return DataPromise<GattCharacteristic>.FromFailure();

            // Check if index is out of bounds
            if (characteristicIndex >= characteristics.Data.Count)
                return DataPromise<GattCharacteristic>.FromFailure();

            // Get characteristic
            GattCharacteristic characteristic = characteristics.Data[characteristicIndex];

            // Return characteristic
            return DataPromise.FromSuccess(characteristic);
        }

        /// <summary>
        /// Get all characteristics from specified service (by UUID) that match specified characteristic UUID
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>List of characteristics or null if not found</returns>
        public DataPromise<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsFor(
            Guid serviceUUID,
            Guid characteristicUUID)
        {
            // Get service
            DataPromise<GattDeviceService> service = GetService(serviceUUID);

            // Check if service is null and return
            return !service.HasData
                ? DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure()
                : GetAllCharacteristicsFor(service.Data, characteristicUUID);
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        public DataPromise<IReadOnlyList<GattCharacteristic>> GetAllCharacteristicsFor(
            GattDeviceService service,
            Guid characteristicUUID)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();

            // Get specific characteristic
            IAsyncOperation<GattCharacteristicsResult> characteristicsRequest =
                service.GetCharacteristicsForUuidAsync(characteristicUUID);

            // Wait for async operation to complete
            while (characteristicsRequest.Status != AsyncStatus.Completed)
            {
                // Check if operation was cancelled or errored
                if (characteristicsRequest.Status is not (AsyncStatus.Canceled or AsyncStatus.Error)) continue;
                return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
            }

            // Get data from async operation
            GattCharacteristicsResult characteristics = characteristicsRequest.GetResults();

            // Check if characteristic is unreachable
            switch (characteristics.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    Disconnect();
                    return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
                case GattCommunicationStatus.Success: break;
                default: return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
            }

            // Return characteristic
            return DataPromise.FromSuccess(characteristics.Characteristics);
        }

        /// <summary>
        /// Get all characteristics from service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of characteristics or null if not found</returns>
        public DataPromise<IReadOnlyList<GattCharacteristic>> GetAllCharacteristics(Guid serviceUUID)
        {
            DataPromise<GattDeviceService> service = GetService(serviceUUID);

            // Check if service is null
            return !service.HasData
                ? DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure()
                : GetAllCharacteristics(service.Data);
        }

        /// <summary>
        /// Get all characteristics from service
        /// </summary>
        /// <param name="service">Service to get characteristics from</param>
        /// <returns>List of characteristics or null if not found</returns>
        public DataPromise<IReadOnlyList<GattCharacteristic>> GetAllCharacteristics(GattDeviceService service)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();

            // Get characteristics
            IAsyncOperation<GattCharacteristicsResult>? characteristicsRequest = service.GetCharacteristicsAsync();

            // Wait for async operation to complete
            while (characteristicsRequest.Status != AsyncStatus.Completed)
            {
                // Check if operation was cancelled or errored
                if (characteristicsRequest.Status is not (AsyncStatus.Canceled or AsyncStatus.Error)) continue;
                return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
            }

            // Get data from async operation
            GattCharacteristicsResult characteristics = characteristicsRequest.GetResults();

            // Check if result is unreachable
            switch (characteristics.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    Disconnect();
                    return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
                case GattCommunicationStatus.Success: break;
                default: return DataPromise<IReadOnlyList<GattCharacteristic>>.FromFailure();
            }

            // Return characteristics
            return DataPromise.FromSuccess(characteristics.Characteristics);
        }

        /// <summary>
        /// Get service for specified UUID
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>Service or null if not found</returns>
        public DataPromise<GattDeviceService> GetService(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise.FromFailure<GattDeviceService>();

            // Check if device exists
            if (ConnectedDevice == null) return DataPromise.FromFailure<GattDeviceService>();

            // Get services
            DataPromise<IReadOnlyList<GattDeviceService>> services = GetServices(serviceUUID);

            // Check if services have data
            if (!services.HasData) return DataPromise.FromFailure<GattDeviceService>();

            // Check if services are empty
            if(services.Data.Count == 0)
                return DataPromise.FromFailure<GattDeviceService>();
            
            // Get first service
            GattDeviceService? service = services.Data?[0];

            // Check if service is null and return
            return service == null
                ? DataPromise.FromFailure<GattDeviceService>()
                : DataPromise.FromSuccess(service);
        }

        /// <summary>
        /// Get services from connected device
        /// </summary>
        /// <param name="serviceUUID">UUID of the service</param>
        /// <returns>List of services</returns>
        public DataPromise<IReadOnlyList<GattDeviceService>> GetServices(Guid serviceUUID)
        {
            // Check if device is connected
            if (!IsConnected) return DataPromise<IReadOnlyList<GattDeviceService>>.FromFailure();

            // Check if device exists
            if (ConnectedDevice == null) return DataPromise<IReadOnlyList<GattDeviceService>>.FromFailure();

            // Get service using UUID
            IAsyncOperation<GattDeviceServicesResult> servicesRequest =
                ConnectedDevice.GetGattServicesForUuidAsync(serviceUUID);

            // Wait for async operation to complete
            while (servicesRequest.Status != AsyncStatus.Completed)
            {
                // Check if operation was cancelled or errored
                if (servicesRequest.Status is not (AsyncStatus.Canceled or AsyncStatus.Error)) continue;
                return DataPromise<IReadOnlyList<GattDeviceService>>.FromFailure();
            }

            // Get data from async operation
            GattDeviceServicesResult servicesResult = servicesRequest.GetResults();

            // Check if service is found
            switch (servicesResult.Status)
            {
                case GattCommunicationStatus.Unreachable:
                    DeviceConnectionLost?.Invoke(DeviceAddress);
                    Disconnect();
                    return DataPromise<IReadOnlyList<GattDeviceService>>.FromFailure();
                case GattCommunicationStatus.Success: break;
                default: return DataPromise<IReadOnlyList<GattDeviceService>>.FromFailure();
            }

            // Return first service found
            return DataPromise.FromSuccess(servicesResult.Services);
        }
    }
}