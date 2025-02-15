namespace IRIS.Bluetooth.Data
{
    /// <summary>
    /// Represents a potential endpoint for a Bluetooth connection for devices which have different
    /// endpoints and different services
    /// </summary>
    public readonly struct PotentialEndpoint(Guid serviceUUID, params Guid[] characteristicUUIDs)
    {
        /// <summary>
        /// Endpoint service UUID
        /// </summary>
        public Guid ServiceUUID { get; } = serviceUUID;
        
        /// <summary>
        /// List of characteristic UUIDs for the endpoint
        /// </summary>
        public Guid[] CharacteristicUUIDs { get; } = characteristicUUIDs;

    }
}