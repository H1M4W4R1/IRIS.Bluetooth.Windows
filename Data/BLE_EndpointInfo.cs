using IRIS.Bluetooth.Communication;

namespace IRIS.Bluetooth.Data
{
    public struct BLE_EndpointInfo(uint endpointIndex, BLE_Endpoint? endpoint, EndpointMode mode = EndpointMode.Required)
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
        public BLE_Endpoint? Endpoint { get; set; } = endpoint;
        
        /// <summary>
        /// List of notification handlers for the endpoint
        /// </summary>
        public List<BLE_Endpoint.NotificationReceivedHandler> NotificationHandlers { get; } = new();
        
        public async void AddNotificationHandler(BLE_Endpoint.NotificationReceivedHandler handler)
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
                await Endpoint.SetNotify(true);
            }
        }
        
        public async void RemoveNotificationHandler(BLE_Endpoint.NotificationReceivedHandler handler)
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
                await Endpoint.SetNotify(false);
            }
        }
    }
}