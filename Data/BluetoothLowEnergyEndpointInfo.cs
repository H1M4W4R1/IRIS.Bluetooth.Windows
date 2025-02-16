using IRIS.Bluetooth.Communication;

namespace IRIS.Bluetooth.Data
{
    public struct BluetoothLowEnergyEndpointInfo(uint endpointIndex, BluetoothLowEnergyEndpoint? endpoint, EndpointMode mode = EndpointMode.Required)
    {
        /// <summary>
        /// Index of the endpoint
        /// </summary>
        public uint EndpointIndex { get; } = endpointIndex;
        
        /// <summary>
        /// Mode of the endpoint
        /// </summary>
        public EndpointMode Mode { get; } = mode;
        
        /// <summary>
        /// Endpoint object
        /// </summary>
        public BluetoothLowEnergyEndpoint? Endpoint { get; set; } = endpoint;
        
        /// <summary>
        /// List of notification handlers for the endpoint
        /// </summary>
        public List<BluetoothLowEnergyEndpoint.NotificationReceivedHandler> NotificationHandlers { get; } = new();
        
        public void AddNotificationHandler(BluetoothLowEnergyEndpoint.NotificationReceivedHandler handler)
        {
            if(Endpoint == null) return;
            if(!Endpoint.IsNotifyAvailable) return;
            
            Endpoint.NotificationReceived += handler;
            
            if(!NotificationHandlers.Contains(handler))
                NotificationHandlers.Add(handler);
            
            // Check if notification handles are larger than 0
            if(NotificationHandlers.Count > 0)
            {
                // Set the endpoint to notify all handlers
                Endpoint.SetNotify(true);
            }
        }
        
        public void RemoveNotificationHandler(BluetoothLowEnergyEndpoint.NotificationReceivedHandler handler)
        {
            if(Endpoint == null) return;
            if(!Endpoint.IsNotifyAvailable) return;
            
            Endpoint.NotificationReceived -= handler;

            // Remove the handler from the list (if exists)
            NotificationHandlers.Remove(handler);
            
            // Check if notification handles exist
            if(NotificationHandlers.Count <= 0)
            {
                // Set the endpoint to stop notifying
                Endpoint.SetNotify(false);
            }
        }
    }
}