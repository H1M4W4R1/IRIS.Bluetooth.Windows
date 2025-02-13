using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Communication;
using IRIS.Bluetooth.Data;
using IRIS.Devices;

namespace IRIS.Bluetooth.Devices
{
    /// <summary>
    /// Represents a Bluetooth LE device
    /// </summary>
    public abstract class BLE_DeviceBase : DeviceBase<BluetoothLEInterface>
    {
        /// <summary>
        /// List of endpoints for the device
        /// TODO: Combine those dictionaries and add boolean to check if endpoint is required to exist
        ///       to allow simple check for endpoint existence after attaching
        /// </summary>
        private Dictionary<uint, BluetoothEndpoint?> Endpoints { get; } =
            new();

        /// <summary>
        /// List of notification handlers for the device
        /// </summary>
        private Dictionary<uint, List<BluetoothEndpoint.NotificationReceivedHandler>>
            NotificationHandlers { get; } =
            new();

        /// <summary>
        /// Determines if the device is connected
        /// </summary>
        public BluetoothDeviceState DeviceState { get; private set; } 
        
        /// <summary>
        /// Check if the device is connected
        /// </summary>
        public bool IsConnected => DeviceState == BluetoothDeviceState.Connected;

        public BLE_DeviceBase(string deviceNameRegex)
        {
            HardwareAccess = new BluetoothLEInterface(deviceNameRegex);
        }

        public BLE_DeviceBase(Guid serviceUUID)
        {
            HardwareAccess = new BluetoothLEInterface(serviceUUID);
        }

        /// <summary>
        /// Used to attach to endpoints events
        /// </summary>
        protected virtual Task AttachOrLoadEndpoints()
            => Task.CompletedTask;

        /// <summary>
        /// Used to detach from endpoints events
        /// </summary>
        private async Task DetachOrUnloadAllEndpoints()
        {
            // Loop through all endpoints and detach from events
            foreach (KeyValuePair<uint, BluetoothEndpoint?> endpoint in Endpoints)
                await DetachOrUnloadEndpoint(endpoint.Key);
        }

        public sealed override async Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            // Check if device is already connected
            if (IsConnected) return true;
            
            // Begin connection
            DeviceState = BluetoothDeviceState.Connecting;

            // Try to connect to device
            // We don't need to subscribe to device connected event as
            // interface will wait for device to be connected
            if (!await base.Connect(cancellationToken)) return false;

            // Connection successful
            DeviceState = BluetoothDeviceState.Connected;
            
            // Handle disconnection
            HardwareAccess.OnDeviceDisconnected += HandleCommunicationFailed;

            // Attach to endpoints
            await AttachOrLoadEndpoints();
            return true;
        }

        /// <summary>
        /// Disconnect from the device
        /// </summary>
        public sealed override async Task<bool> Disconnect(CancellationToken cancellationToken = default)
        {
            // Begin disconnection
            DeviceState = BluetoothDeviceState.Disconnecting;
            
            HardwareAccess.OnDeviceDisconnected -= HandleCommunicationFailed;

            // Detach from endpoints
            await DetachOrUnloadAllEndpoints();
            
            // Guarantee that all endpoints and notification handlers are cleared
            Endpoints.Clear();
            NotificationHandlers.Clear();

            if (!await base.Disconnect(cancellationToken)) return false;

            // Disconnection successful
            DeviceState = BluetoothDeviceState.Disconnected;
            return true;
        }

        private async void HandleCommunicationFailed(ulong address, BluetoothLEDevice device)
        {
            await Disconnect();
        }

        /// <summary>
        /// Get an endpoint by index
        /// </summary>
        /// <param name="endpointIndex">Endpoint index</param>
        /// <returns>Endpoint if found, null otherwise</returns>
        protected BluetoothEndpoint? GetEndpoint(uint endpointIndex) =>
            Endpoints.GetValueOrDefault(endpointIndex);

        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristicIndex">Characteristic index</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <returns>True if successful, false otherwise</returns>
        protected async Task<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            BluetoothEndpoint.NotificationReceivedHandler notificationHandler)
        {
            BluetoothEndpoint? endpoint = await FindEndpoint(endpointService, endpointCharacteristicIndex);
            return await _AttachEndpoint(endpointIndex, endpoint, notificationHandler);
        }

        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <returns>True if successful, false otherwise</returns>
        protected async Task<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            BluetoothEndpoint.NotificationReceivedHandler notificationHandler)
        {
            // Get endpoint
            BluetoothEndpoint? endpoint = await FindEndpoint(endpointService, endpointCharacteristic);
            return await _AttachEndpoint(endpointIndex, endpoint, notificationHandler);
        }

        /// <summary>
        /// Detach from an endpoint
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        private async Task DetachOrUnloadEndpoint(uint endpointIndex)
        {
            // Check if endpoint exists
            if (!Endpoints.TryGetValue(endpointIndex, out BluetoothEndpoint? endpoint)) return;

            // Check if endpoint is null
            if (endpoint == null) return;

            // Get notifications
            if (NotificationHandlers.TryGetValue(endpointIndex,
                    out List<BluetoothEndpoint.NotificationReceivedHandler>? notificationHandlers))
            {
                // We found notification handlers, so we need to detach them
                foreach (BluetoothEndpoint.NotificationReceivedHandler notificationHandler in notificationHandlers)
                    endpoint.NotificationReceived -= notificationHandler;
                
                // Clear notification handlers
                NotificationHandlers.Remove(endpointIndex);
            }
            
            // Detach from endpoint
            await endpoint.SetNotify(false);
            
            // Remove endpoint
            Endpoints.Remove(endpointIndex);
        }

        /// <summary>
        /// Internal method to attach to an endpoint
        /// </summary>
        private async Task<bool> _AttachEndpoint(
            uint endpointIndex,
            BluetoothEndpoint? endpoint,
            BluetoothEndpoint.NotificationReceivedHandler notificationHandler)
        {
            // Check if endpoint already exists
            if (!Endpoints.TryAdd(endpointIndex, endpoint))
            {
                // Update endpoint if it is not null and the current endpoint is null
                if (endpoint != null && Endpoints[endpointIndex] == null)
                    Endpoints[endpointIndex] = endpoint;
                else
                {
                    Debug.WriteLine($"Endpoint {endpointIndex} already exists on device {GetType().Name}");
                    return false;
                }
            }

            // Attach to endpoint events if endpoint is not null and supports notify
            if (endpoint == null || !await endpoint.SetNotify(true)) return false;
            endpoint.NotificationReceived += notificationHandler;
            
            // Register notification handler
            if (!NotificationHandlers.ContainsKey(endpointIndex))
                NotificationHandlers.Add(endpointIndex, new List<BluetoothEndpoint.NotificationReceivedHandler>());
            NotificationHandlers[endpointIndex].Add(notificationHandler);
            return true;
        }

        /// <summary>
        /// Load endpoint
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        protected async Task LoadEndpoint(uint endpointIndex, Guid endpointService, Guid endpointCharacteristic)
        {
            // Get endpoint
            BluetoothEndpoint? endpoint = await FindEndpoint(endpointService, endpointCharacteristic);

            // Load endpoint
            await LoadEndpoint(endpointIndex, endpoint);
        }

        /// <summary>
        /// Internal method to load an endpoint
        /// </summary>
        protected Task LoadEndpoint(uint endpointIndex, BluetoothEndpoint? endpoint)
        {
            // Check if endpoint already exists
            if (!Endpoints.TryAdd(endpointIndex, endpoint))
            {
                // Update endpoint if it is not null and the current endpoint is null
                if (endpoint != null && Endpoints[endpointIndex] == null)
                    Endpoints[endpointIndex] = endpoint;
                else
                    Debug.WriteLine($"Endpoint {endpointIndex} already exists on device {GetType().Name}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Searches for a service on the device
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <returns>Service if found, null otherwise</returns>
        private async Task<GattDeviceService?> FindService(Guid serviceUUID)
        {
            // Check if interface is connected
            if (!HardwareAccess.IsConnected) return null;

            // Check if interface has valid device
            if (HardwareAccess.ConnectedDevice == null) return null;

            // Get all services for device
            GattDeviceServicesResult services = await HardwareAccess.ConnectedDevice.GetGattServicesAsync();

            // Check if services are valid
            if (services.Status != GattCommunicationStatus.Success) return null;

            // Get the service with the correct UUID
            GattDeviceService? service = services.Services.FirstOrDefault(s => s.Uuid == serviceUUID);

            // Check if service is valid
            return service == null ? null : service;
        }

        /// <summary>
        /// Find the endpoint on a Bluetooth device
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="endpointIndex">Characteristic index</param>
        /// <returns>Endpoint if found, null otherwise</returns>
        private async Task<BluetoothEndpoint?> FindEndpoint(
            Guid serviceUUID,
            int endpointIndex)
        {
            // Get service
            GattDeviceService? service = await FindService(serviceUUID);

            // Check if service is valid
            if (service == null) return null;

            // Get all characteristics for service
            GattCharacteristicsResult characteristics = await service.GetCharacteristicsAsync();

            // Check if characteristics are valid
            if (characteristics.Status != GattCommunicationStatus.Success) return null;

            // Get the characteristic with the correct index
            GattCharacteristic? characteristic = characteristics.Characteristics.ElementAtOrDefault(endpointIndex);

            // Check if characteristic is valid
            return characteristic == null ? null : new BluetoothEndpoint(HardwareAccess, service, characteristic);
        }

        /// <summary>
        /// Get the endpoint on a Bluetooth device
        /// </summary>
        /// <param name="serviceUUID">Service UUID</param>
        /// <param name="characteristicUUID">Characteristic UUID</param>
        /// <returns>Endpoint if found, null otherwise</returns>
        private async Task<BluetoothEndpoint?> FindEndpoint(
            Guid serviceUUID,
            Guid characteristicUUID)
        {
            // Get service
            GattDeviceService? service = await FindService(serviceUUID);

            // Check if service is valid
            if (service == null) return null;

            // Get all characteristics for service
            GattCharacteristicsResult characteristics = await service.GetCharacteristicsAsync();

            // Check if characteristics are valid
            if (characteristics.Status != GattCommunicationStatus.Success) return null;

            // Get the characteristic with the correct UUID
            GattCharacteristic? characteristic =
                characteristics.Characteristics.FirstOrDefault(c => c.Uuid == characteristicUUID);

            // Check if characteristic is valid
            return characteristic == null
                ? null
                : new BluetoothEndpoint(HardwareAccess, service, characteristic);
        }
    }
}