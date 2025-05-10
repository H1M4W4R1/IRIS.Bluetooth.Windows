using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Data;

namespace IRIS.Bluetooth.Communication
{
    /// <summary>
    /// Represents a Bluetooth Low Energy Serial connection
    /// </summary>
    public sealed class BluetoothLowEnergySerial(BluetoothLowEnergyEndpoint tx, BluetoothLowEnergyEndpoint rx)
    {
        private readonly BluetoothLowEnergyEndpoint? _txEndpoint = tx;
        private readonly BluetoothLowEnergyEndpoint? _rxEndpoint = rx;
        
        /// <summary>
        /// Writes a string to the TX endpoint
        /// </summary>
        /// <param name="message">>String to write to the TX endpoint</param>
        public void Write(string message) => _txEndpoint?.Write(message);

        /// <summary>
        /// Writes raw data to the TX endpoint
        /// </summary>
        /// <param name="data">>Raw data to write to the TX endpoint</param>
        public void WriteRawData(byte[] data) => _txEndpoint?.Write(data);

        /// <summary>
        /// Reads raw data from the RX endpoint
        /// </summary>
        /// <returns>>Raw data read from the RX endpoint or null if no data is available</returns>
        public byte[]? ReadRawData()
        {
            if (_rxEndpoint is null) return null;

            // Read data from the RX endpoint
            return _rxEndpoint.ReadData<byte[]>();
        }

        /// <summary>
        /// Reads a string from the RX endpoint
        /// </summary>
        /// <returns>String read from the RX endpoint or null if no data is available</returns>
        public string? Read()
        {
            if (_rxEndpoint is null) return null;

            // Read data from the RX endpoint
            return _rxEndpoint.ReadData<string>();
        }

        /// <summary>
        /// Writes a string to the TX endpoint and reads a string from the RX endpoint
        /// </summary>
        /// <param name="message">String to write to the TX endpoint</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>>String read from the RX endpoint or null if no data is available</returns>
        public string? WriteRead(string message, CancellationToken cancellationToken = default)
        {
            if (_txEndpoint is null || _rxEndpoint is null) return null;

            BluetoothLowEnergyEndpoint rxEndpointReference = _rxEndpoint;
            string? messageResponse = null;
            bool isResponseReceived = false;
            
            // Attach the RX handler to the RX endpoint
            rxEndpointReference.NotificationReceived += WaitForResponse;
            
            // Write data to the TX endpoint
            _txEndpoint.Write(message);
            
            // Wait for the response
            while (!isResponseReceived)
            {
                // Check if cancellation is requested
                if (!cancellationToken.IsCancellationRequested) continue;
                
                // Detach the RX handler from the RX endpoint and return null
                // after cancellation
                rxEndpointReference.NotificationReceived -= WaitForResponse;
                return null;
            }

            // Ensure the RX handler is detached from the RX endpoint
            rxEndpointReference.NotificationReceived -= WaitForResponse;
            
            // Return the response
            return messageResponse;

            // Internal method to handle the RX endpoint notification received event
            void WaitForResponse(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                // Read data from the RX endpoint
                string? response = rxEndpointReference.ReadData<string>();
                
                // Detach the RX handler from the RX endpoint
                rxEndpointReference.NotificationReceived -= WaitForResponse;

                // Check if data is available
                if (response == null) return;
            
                // Return the response
                messageResponse = response;
                isResponseReceived = true;
            }
        }
        
        /// <summary>
        /// Attaches a handler to the RX endpoint for notification received events
        /// </summary>
        /// <param name="handler">>Handler to attach to the RX endpoint</param>
        public void AttachRXHandler(BluetoothLowEnergyEndpoint.NotificationReceivedHandler handler)
        {
            if (_rxEndpoint is null) return;

            // Attach the handler to the RX endpoint
            _rxEndpoint.NotificationReceived += handler;
        }
        
        /// <summary>
        /// Detaches a handler from the RX endpoint for notification received events
        /// </summary>
        /// <param name="handler">>Handler to detach from the RX endpoint</param>
        public void DetachRXHandler(BluetoothLowEnergyEndpoint.NotificationReceivedHandler handler)
        {
            if (_rxEndpoint is null) return;

            // Detach the handler from the RX endpoint
            _rxEndpoint.NotificationReceived -= handler;
        }
    }
}