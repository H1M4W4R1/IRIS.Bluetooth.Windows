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
    public abstract class BluetoothLowEnergyDeviceBase : DeviceBase<BluetoothLowEnergyInterface>
    {
        /// <summary>
        /// List of all known endpoints registered on this device
        /// </summary>
        private List<BluetoothLowEnergyEndpointInfo> Endpoints { get; } = new();

        /// <summary>
        /// Determines if the device is connected
        /// </summary>
        public BluetoothDeviceState DeviceState { get; private set; }

        /// <summary>
        /// Check if the device is connected
        /// </summary>
        public bool IsConnected => DeviceState == BluetoothDeviceState.Connected;

        public BluetoothLowEnergyDeviceBase(string deviceNameRegex)
        {
            HardwareAccess = new BluetoothLowEnergyInterface(deviceNameRegex);
        }

        public BluetoothLowEnergyDeviceBase(Guid serviceUUID)
        {
            HardwareAccess = new BluetoothLowEnergyInterface(serviceUUID);
        }

        /// <summary>
        /// Used to attach to endpoints events
        /// </summary>
        protected virtual ValueTask AttachOrLoadEndpoints()
            => ValueTask.CompletedTask;

        /// <summary>
        /// Used to detach from endpoints events
        /// </summary>
        private async ValueTask DetachOrUnloadAllEndpoints()
        {
            // Loop through all endpoints and detach from events
            for (int index = 0; index < Endpoints.Count; index++)
            {
                await DetachOrUnloadEndpoint(index);
            }
        }

        public sealed override async ValueTask<bool> Connect(CancellationToken cancellationToken = default)
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
        public sealed override async ValueTask<bool> Disconnect(CancellationToken cancellationToken = default)
        {
            // Begin disconnection
            DeviceState = BluetoothDeviceState.Disconnecting;

            HardwareAccess.OnDeviceDisconnected -= HandleCommunicationFailed;

            // Detach from endpoints
            await DetachOrUnloadAllEndpoints();

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
        protected BluetoothLowEnergyEndpoint? GetEndpoint(uint endpointIndex) => GetEndpointInfo(endpointIndex)?.Endpoint;

        /// <summary>
        /// Get endpoint info by index
        /// </summary>
        protected BluetoothLowEnergyEndpointInfo? GetEndpointInfo(uint endpointIndex)
        {
            // Loop through all endpoints and find the one with the same index
            foreach (BluetoothLowEnergyEndpointInfo endpoint in Endpoints)
            {
                if (endpoint.EndpointIndex == endpointIndex) return endpoint;
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
            foreach (BluetoothLowEnergyEndpointInfo endpoint in Endpoints)
            {
                if (endpoint is {Mode: EndpointMode.Required, Endpoint: null}) return false;
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
        protected async ValueTask<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            BluetoothLowEnergyEndpoint? endpoint =
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
        protected async ValueTask<bool> AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            BluetoothLowEnergyEndpoint? endpoint = await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);

            return _AttachEndpoint(endpointIndex, endpoint, notificationHandler);
        }

        /// <summary>
        /// Internal method to attach to an endpoint
        /// </summary>
        private bool _AttachEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint? endpoint,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            lock (Endpoints)
            {
                // Check if endpoint already exists add notification handler and return
                BluetoothLowEnergyEndpointInfo foundEndpoint = Endpoints.FirstOrDefault(x => x.EndpointIndex == endpointIndex);
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

        // TODO: Load any endpoint by GUID or GUID pair (service:endpoint)
        // TODO: Attach to any endpoint by GUID or GUID pair (service:endpoint)

        /// <summary>
        /// Load endpoint, if you want to attach to notifications, use AttachEndpoint instead
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristicIndex">Characteristic index</param>
        /// <param name="mode">Mode of the endpoint</param>
        protected async ValueTask LoadEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            EndpointMode mode = EndpointMode.Required)
        {
            BluetoothLowEnergyEndpoint? endpoint =
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
        protected async ValueTask LoadEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            BluetoothLowEnergyEndpoint? endpoint = await HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);

            // Load endpoint
            _LoadEndpoint(endpointIndex, endpoint, mode);
        }

        /// <summary>
        /// Internal method to load an endpoint
        /// </summary>
        private void _LoadEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint? endpoint,
            EndpointMode mode = EndpointMode.Required)
        {
            // If no endpoint with same ID exists add it
            if (Endpoints.All(x => x.EndpointIndex != endpointIndex))
            {
                Endpoints.Add(new BluetoothLowEnergyEndpointInfo(endpointIndex, endpoint, mode));
                return;
            }

            // Search for first existing endpoint and replace it
            // if endpoint is null
            for (int i = 0; i < Endpoints.Count; i++)
            {
                if (Endpoints[i].EndpointIndex == endpointIndex && Endpoints[i].Endpoint == null)
                {
                    Endpoints[i] = new BluetoothLowEnergyEndpointInfo(endpointIndex, endpoint, mode);
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
        private async ValueTask DetachOrUnloadEndpoint(int listIndex)
        {
            BluetoothLowEnergyEndpointInfo endpointInfo;
            
            lock (Endpoints)
            {
                // Check if endpoint exists
                if (listIndex < 0 || listIndex >= Endpoints.Count) return;

                // Get endpoint info
                endpointInfo = Endpoints[listIndex];

                // Remove endpoint
                Endpoints.RemoveAt(listIndex);

                // Check if endpoint exists
                if (endpointInfo.Endpoint == null) return;

                // We found notification handlers, so we need to detach them
                foreach (BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler in endpointInfo
                             .NotificationHandlers)
                {
                    endpointInfo.Endpoint.NotificationReceived -= notificationHandler;
                }

                // Clear notification handlers
                endpointInfo.NotificationHandlers.Clear();
            }

            // Detach from endpoint, endpointInfo will be valid as otherwise lock statement will enforce
            // that we will not reach this point
            await endpointInfo.Endpoint.SetNotify(false);
        }
    }
}