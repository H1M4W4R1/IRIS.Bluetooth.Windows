using System.Diagnostics;
using Windows.Devices.Bluetooth;
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
        /// List of all known endpoints registered on this device
        /// </summary>
        private List<BLE_EndpointInfo> Endpoints { get; } = new();

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
        private void DetachOrUnloadAllEndpoints()
        {
            // Loop through all endpoints and detach from events
            for (int index = 0; index < Endpoints.Count; index++)
            {
                DetachOrUnloadEndpoint(index);
            }
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

            // Ensure that all required endpoints are attached
            if (CheckIfAllRequiredEndpointsAreValid()) return true;
            
            // Disconnect if required endpoints are not attached
            await Disconnect(cancellationToken);
            return false;
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
            DetachOrUnloadAllEndpoints();

            // Guarantee that all endpoints and notification handlers are cleared
            Endpoints.Clear();

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
        protected BLE_Endpoint? GetEndpoint(uint endpointIndex) => GetEndpointInfo(endpointIndex)?.Endpoint;

        /// <summary>
        /// Get endpoint info by index
        /// </summary>
        protected BLE_EndpointInfo? GetEndpointInfo(uint endpointIndex)
        {
            // Loop through all endpoints and find the one with the same index
            foreach (BLE_EndpointInfo endpoint in Endpoints)
            {
                if (endpoint.EndpointIndex == endpointIndex)
                    return endpoint;
            }
            
            return null;
        }

        /// <summary>
        /// Check if all required endpoints are attached / loaded
        /// </summary>
        /// <returns>True if all required endpoints are attached, false otherwise</returns>
        protected bool CheckIfAllRequiredEndpointsAreValid()
        {
            // Loop through all endpoints and check if all required endpoints are attached
            foreach (BLE_EndpointInfo endpoint in Endpoints)
            {
                if (endpoint is {Mode: EndpointMode.Required, Endpoint: null})
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristicIndex">Characteristic index</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <returns>True if successful, false otherwise</returns>
        protected async Task<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            BLE_Endpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            BLE_Endpoint? endpoint =
                await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristicIndex);
            
            return _AttachEndpoint(endpointIndex, endpoint, notificationHandler);
        }

        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <returns>True if successful, false otherwise</returns>
        protected async Task<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            BLE_Endpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            BLE_Endpoint? endpoint = await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);
            
            return _AttachEndpoint(endpointIndex, endpoint, notificationHandler);
        }

        /// <summary>
        /// Internal method to attach to an endpoint
        /// </summary>
        private bool _AttachEndpoint(
            uint endpointIndex,
            BLE_Endpoint? endpoint,
            BLE_Endpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            lock (Endpoints)
            {
                // Check if endpoint already exists add notification handler and return
                BLE_EndpointInfo foundEndpoint = Endpoints.FirstOrDefault(x => x.EndpointIndex == endpointIndex);
                if (foundEndpoint.Endpoint is {IsNotifyAvailable: true})
                {
                    foundEndpoint.AddNotificationHandler(notificationHandler);
                    return true;
                }

                // Endpoint exists but does not support notifications
                if (foundEndpoint.Endpoint != null) return false;

                // Add endpoint
                _LoadEndpoint(endpointIndex, endpoint, mode);

                // Attach notification handler
                Endpoints[^1].AddNotificationHandler(notificationHandler);
                return true;
            }
        }

        /// <summary>
        /// Load endpoint, if you want to attach to notifications, use AttachEndpoint instead
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristicIndex">Characteristic index</param>
        /// <param name="mode">Mode of the endpoint</param>
        protected async Task LoadEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            EndpointMode mode = EndpointMode.Required)
        {
            BLE_Endpoint? endpoint =
                await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristicIndex);
            
            _LoadEndpoint(endpointIndex, endpoint, mode);
        }

        /// <summary>
        /// Load endpoint, if you want to attach to notifications, use AttachEndpoint instead
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        /// <param name="mode">Mode of the endpoint</param>
        protected async Task LoadEndpoint(uint endpointIndex, Guid endpointService, Guid endpointCharacteristic,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            BLE_Endpoint? endpoint = await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);

            // Load endpoint
            _LoadEndpoint(endpointIndex, endpoint, mode);
        }

        /// <summary>
        /// Internal method to load an endpoint
        /// </summary>
        private void _LoadEndpoint(uint endpointIndex, BLE_Endpoint? endpoint, EndpointMode mode = EndpointMode.Required)
        {
            // If no endpoint with same ID exists add it
            if (Endpoints.All(x => x.EndpointIndex != endpointIndex))
            {
                Endpoints.Add(new BLE_EndpointInfo(endpointIndex, endpoint, mode));
                return;
            }
            
            // Search for first existing endpoint and replace it
            // if endpoint is null
            for (int i = 0; i < Endpoints.Count; i++)
            {
                if (Endpoints[i].EndpointIndex == endpointIndex && Endpoints[i].Endpoint == null)
                {
                    Endpoints[i] = new BLE_EndpointInfo(endpointIndex, endpoint, mode);
                    return;
                }
                else
                {
                    Debug.WriteLine("Endpoint with same index already exists");
                }
            }
        }

        /// <summary>
        /// Detach from an endpoint
        /// </summary>
        /// <param name="listIndex">Index of the endpoint in endpoints list</param>
        private void DetachOrUnloadEndpoint(int listIndex)
        {
            lock (Endpoints)
            {
                // Check if endpoint exists
                if (listIndex < 0 || listIndex >= Endpoints.Count) return;

                // Get endpoint info
                BLE_EndpointInfo endpointInfo = Endpoints[listIndex];

                // Check if endpoint exists
                if (endpointInfo.Endpoint == null) return;


                // We found notification handlers, so we need to detach them
                foreach (BLE_Endpoint.NotificationReceivedHandler notificationHandler in endpointInfo
                             .NotificationHandlers)
                {
                    endpointInfo.Endpoint.NotificationReceived -= notificationHandler;
                }

                // Clear notification handlers
                endpointInfo.NotificationHandlers.Clear();

                // Detach from endpoint
                endpointInfo.Endpoint.SetNotify(false).Wait();

                // Remove endpoint
                Endpoints.RemoveAt(listIndex);
            }
        }
    }
}