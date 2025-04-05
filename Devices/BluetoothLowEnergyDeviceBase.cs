using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Communication;
using IRIS.Bluetooth.Data;
using IRIS.Data;
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

        /// <summary>
        /// Check if the device is connecting
        /// </summary>
        public bool IsConnectedOrConnecting => DeviceState != BluetoothDeviceState.Disconnected &&
                                               DeviceState != BluetoothDeviceState.Disconnecting;

        /// <summary>
        /// Flag to check if endpoints are attached
        /// </summary>
        public bool AreEndpointsAttached { get; private set; }

        public BluetoothLowEnergyDeviceBase(string deviceNameRegex)
        {
            HardwareAccess = new BluetoothLowEnergyInterface(deviceNameRegex);
        }

        public BluetoothLowEnergyDeviceBase(Guid serviceUUID)
        {
            HardwareAccess = new BluetoothLowEnergyInterface(serviceUUID);
        }
        
        public BluetoothLowEnergyDeviceBase(ulong deviceAddress)
        {
            HardwareAccess = new BluetoothLowEnergyInterface(deviceAddress);
        }

        /// <summary>
        /// Used to attach to endpoints events
        /// </summary>
        protected virtual void AttachOrLoadEndpoints()
        {
        }

        /// <summary>
        /// Called when the device is connected successfully
        /// </summary>
        protected virtual void OnSuccessfulConnection()
        {
        }

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

        public sealed override bool Connect(CancellationToken cancellationToken = default)
        {
            // Check if device is already connected
            if (IsConnected) return true;

            // Begin connection
            DeviceState = BluetoothDeviceState.Connecting;

            // Try to connect to device
            // We don't need to subscribe to device connected event as
            // interface will wait for device to be connected
            if (!base.Connect(cancellationToken)) return false;

            // Handle disconnection
            HardwareAccess.BluetoothDeviceDisconnected += HandleCommunicationFailed;

            // Search for endpoints 
            DeviceState = BluetoothDeviceState.SearchingForEndpoints;

            // Attach to endpoints
            AttachOrLoadEndpoints();

            // Ensure that all required endpoints are attached
            if (CheckIfAllRequiredEndpointsAreValid())
            {
                OnSuccessfulConnection();

                // Connection successful
                DeviceState = BluetoothDeviceState.Connected;

                return true;
            }

            // Disconnect if required endpoints are not attached
            Disconnect(cancellationToken);
            return false;
        }

        /// <summary>
        /// Disconnect from the device
        /// </summary>
        public sealed override bool Disconnect(CancellationToken cancellationToken = default)
        {
            // Begin disconnection
            DeviceState = BluetoothDeviceState.Disconnecting;
            AreEndpointsAttached = false;

            HardwareAccess.BluetoothDeviceDisconnected -= HandleCommunicationFailed;

            // Detach from endpoints
            DetachOrUnloadAllEndpoints();

            // Guarantee that all endpoints and notification handlers are cleared
            Endpoints.Clear();

            if (!base.Disconnect(cancellationToken)) return false;

            // Disconnection successful
            DeviceState = BluetoothDeviceState.Disconnected;
            return true;
        }

        private void HandleCommunicationFailed(ulong address, BluetoothLEDevice device)
        {
            Disconnect();
        }

        /// <summary>
        /// Get an endpoint by index
        /// </summary>
        /// <param name="endpointIndex">Endpoint index</param>
        /// <returns>Endpoint if found, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? GetEndpoint(uint endpointIndex)
            => GetEndpointInfo(endpointIndex)?.Endpoint;

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
        /// Attach to an notify-type endpoint using the service UUID
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <param name="serviceUUIDs">Service UUIDs to attach to</param>
        /// <returns>>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? AttachRXEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required,
            params Guid[] serviceUUIDs)
        {
            // Loop through all service UUIDs and try to load the endpoint
            foreach (Guid serviceUUID in serviceUUIDs)
            {
                // Get all endpoints for the service UUID
                DataPromise<IReadOnlyList<GattCharacteristic>> characteristicUUIDs =
                    HardwareAccess.GetAllCharacteristics(serviceUUID);

                // Check if characteristic UUIDs are null
                if (!characteristicUUIDs.HasData) continue;

                // Loop through all characteristic UUIDs and try to load the endpoint if it can be notified
                BluetoothLowEnergyEndpoint? endpoint = null;
                foreach (GattCharacteristic characteristic in characteristicUUIDs.Data)
                {
                    // Check if characteristic can be notified, if so, attach to it
                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                        endpoint = AttachEndpoint(endpointIndex, serviceUUID, characteristic.Uuid,
                            notificationHandler, mode);
                }

                // Return endpoint if successful
                if (endpoint != null) return endpoint;
            }

            // Return null if no endpoint was found
            return null;
        }

        /// <summary>
        /// Load a TX endpoint using the service UUID
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <param name="serviceUUIDs">Service UUIDs to load</param>
        /// <returns>>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? LoadTXEndpoint(
            uint endpointIndex,
            EndpointMode mode = EndpointMode.Required,
            params Guid[] serviceUUIDs)
        {
            // Loop through all service UUIDs and try to load the endpoint
            foreach (Guid serviceUUID in serviceUUIDs)
            {
                // Get all endpoints for the service UUID
                DataPromise<IReadOnlyList<GattCharacteristic>> characteristicUUIDs =
                    HardwareAccess.GetAllCharacteristics(serviceUUID);

                // Check if characteristic UUIDs are null
                if (!characteristicUUIDs.HasData) continue;

                // Loop through all characteristic UUIDs and try to load the endpoint if it can be notified
                BluetoothLowEnergyEndpoint? endpoint = null;
                foreach (GattCharacteristic characteristic in characteristicUUIDs.Data)
                {
                    // Check if characteristic can be notified, if so, attach to it
                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                        characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties
                            .WriteWithoutResponse))
                        endpoint = LoadEndpoint(endpointIndex, serviceUUID, characteristic.Uuid, mode);
                }

                // Return endpoint if successful
                if (endpoint != null) return endpoint;
            }

            // Return null if no endpoint was found
            return null;
        }

        /// <summary>
        /// Attach to an endpoint by potential endpoints
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <param name="potentialEndpoints">Potential endpoints to load</param>
        /// <returns>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? AttachEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required,
            params PotentialEndpoint[] potentialEndpoints)
        {
            // Loop through all potential endpoints and try to load them
            foreach (PotentialEndpoint potentialEndpoint in potentialEndpoints)
            {
                // Loop through all characteristic UUIDs and try to load the endpoint
                foreach (Guid characteristicUUID in potentialEndpoint.CharacteristicUUIDs)
                {
                    // Load endpoint
                    BluetoothLowEnergyEndpoint? endpoint = AttachEndpoint(endpointIndex,
                        potentialEndpoint.ServiceUUID, characteristicUUID, notificationHandler, mode);

                    // Return endpoint if successful
                    if (endpoint != null) return endpoint;
                }
            }

            // Return null if no endpoint was found
            return null;
        }

        /// <summary>
        /// Loads endpoint by potential endpoints
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <param name="potentialEndpoints">Potential endpoints to load</param>
        /// <returns>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? LoadEndpoint(
            uint endpointIndex,
            EndpointMode mode = EndpointMode.Required,
            params PotentialEndpoint[] potentialEndpoints)
        {
            // Loop through all potential endpoints and try to load them
            foreach (PotentialEndpoint potentialEndpoint in potentialEndpoints)
            {
                // Loop through all characteristic UUIDs and try to load the endpoint
                foreach (Guid characteristicUUID in potentialEndpoint.CharacteristicUUIDs)
                {
                    // Load endpoint
                    BluetoothLowEnergyEndpoint? endpoint = LoadEndpoint(endpointIndex,
                        potentialEndpoint.ServiceUUID, characteristicUUID, mode);

                    // Return endpoint if successful
                    if (endpoint != null) return endpoint;
                }
            }

            // Return null if no endpoint was found
            return null;
        }

        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristicIndex">Characteristic index</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <returns>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            DataPromise<BluetoothLowEnergyEndpoint> endpoint =
                HardwareAccess.FindEndpoint(endpointService, endpointCharacteristicIndex);

            // Attach endpoint
            return _AttachEndpoint(endpointIndex, endpoint.Data, notificationHandler) ? endpoint.Data : null;
        }

        /// <summary>
        /// Attach to an endpoint
        /// </summary>
        /// <param name="endpointIndex">Endpoint index to create</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        /// <param name="notificationHandler">Notification handler</param>
        /// <param name="mode">Mode of the endpoint</param>
        /// <returns>Endpoint if successful, null otherwise</returns>
        protected BluetoothLowEnergyEndpoint? AttachEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            DataPromise<BluetoothLowEnergyEndpoint> endpoint =
                HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);

            return _AttachEndpoint(endpointIndex, endpoint.Data, notificationHandler) ? endpoint.Data : null;
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
                BluetoothLowEnergyEndpointInfo foundEndpoint =
                    Endpoints.FirstOrDefault(x => x.EndpointIndex == endpointIndex);
                if (foundEndpoint.Endpoint is {IsNotifyAvailable: true})
                {
                    foundEndpoint.AddNotificationHandler(notificationHandler);
                    return true;
                }

                // Endpoint exists but does not support notifications
                if (foundEndpoint.Endpoint != null) return false;

                // Add endpoint
                if (!_LoadEndpoint(endpointIndex, endpoint, mode)) return false;

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
        protected BluetoothLowEnergyEndpoint? LoadEndpoint(
            uint endpointIndex,
            Guid endpointService,
            int endpointCharacteristicIndex,
            EndpointMode mode = EndpointMode.Required)
        {
            DataPromise<BluetoothLowEnergyEndpoint> endpoint =
                HardwareAccess.FindEndpoint(endpointService, endpointCharacteristicIndex);

            return _LoadEndpoint(endpointIndex, endpoint.Data, mode) ? endpoint.Data : null;
        }

        /// <summary>
        /// Load endpoint, if you want to attach to notifications, use AttachEndpoint instead
        /// </summary>
        /// <param name="endpointIndex">Index of the endpoint</param>
        /// <param name="endpointService">Service UUID</param>
        /// <param name="endpointCharacteristic">Characteristic UUID</param>
        /// <param name="mode">Mode of the endpoint</param>
        protected BluetoothLowEnergyEndpoint? LoadEndpoint(
            uint endpointIndex,
            Guid endpointService,
            Guid endpointCharacteristic,
            EndpointMode mode = EndpointMode.Required)
        {
            // Get endpoint
            DataPromise<BluetoothLowEnergyEndpoint> endpoint =
                HardwareAccess.FindEndpoint(endpointService, endpointCharacteristic);

            // Load endpoint
            return _LoadEndpoint(endpointIndex, endpoint.Data, mode) ? endpoint.Data : null;
        }

        /// <summary>
        /// Internal method to load an endpoint
        /// </summary>
        private bool _LoadEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint? endpoint,
            EndpointMode mode = EndpointMode.Required)
        {
            // Search for first existing endpoint and replace it
            // if endpoint is null
            for (int i = 0; i < Endpoints.Count; i++)
            {
                // Skip if endpoint index does not match
                if (Endpoints[i].EndpointIndex != endpointIndex) continue;

                // Check if endpoint already exists and is assigned
                if (Endpoints[i].Endpoint != null)
                {
                    Debug.WriteLine("Endpoint with same index already exists");
                    return false;
                }

                // Do not allocate memory if both endpoints are null
                if (endpoint == null) return true;

                // Update endpoint with new one
                Endpoints[i] = new BluetoothLowEnergyEndpointInfo(endpointIndex, endpoint, mode);
                return true;
            }

            // Add new endpoint
            Endpoints.Add(new BluetoothLowEnergyEndpointInfo(endpointIndex, endpoint, mode));
            return true;
        }

        /// <summary>
        /// Detach from an endpoint
        /// </summary>
        /// <param name="listIndex">Index of the endpoint in endpoints list</param>
        private void DetachOrUnloadEndpoint(int listIndex)
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
            endpointInfo.Endpoint.SetNotify(false);
        }
    }
}