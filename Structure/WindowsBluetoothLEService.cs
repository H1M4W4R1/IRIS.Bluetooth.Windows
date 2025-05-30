using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Operations;
using IRIS.Operations.Abstract;
using IRIS.Operations.Configuration;
using IRIS.Operations.Data;
using IRIS.Operations.Generic;

namespace IRIS.Bluetooth.Windows.Structure
{
    /// <summary>
    ///     Represents a Bluetooth Low Energy (BLE) service implementation for Windows platform.
    ///     This class manages the characteristics associated with a GATT service.
    /// </summary>
    internal sealed class WindowsBluetoothLEService(IBluetoothLEDevice device, GattDeviceService service)
        : IBluetoothLEService
    {
        /// <summary>
        ///     Internal list of characteristics associated with this service.
        /// </summary>
        private readonly List<IBluetoothLECharacteristic> _characteristics = new();

        /// <summary>
        ///     Gets the underlying GATT service that this service is associated with.
        /// </summary>
        internal GattDeviceService? GattService { get; } = service;

        /// <summary>
        ///     Gets the Bluetooth LE device that this service is associated with.
        /// </summary>
        public IBluetoothLEDevice Device { get; } = device;

        /// <summary>
        ///     Gets the unique identifier (UUID) of the service.
        /// </summary>
        public string UUID { get; } = service.Uuid.ToString();

        /// <summary>
        ///     Gets a read-only list of all characteristics associated with this service.
        /// </summary>
        public IReadOnlyList<IBluetoothLECharacteristic> Characteristics => _characteristics;

        /// <summary>
        ///     Initializes the service by retrieving and setting up all characteristics.
        /// </summary>
        internal async ValueTask SetupService()
        {
            // Check if GATT service is null
            if (GattService is null) return;
            
            // Get all characteristics
            GattCharacteristicsResult? characteristics = await GattService.GetCharacteristicsAsync();
            
            // Check if status is success
            if (characteristics.Status is not GattCommunicationStatus.Success) return;
            
            // Construct characteristic list
            foreach (GattCharacteristic gattCharacteristic in characteristics.Characteristics)
            {
                _characteristics.Add(new WindowsBluetoothLECharacteristic(this, gattCharacteristic));
            }
        }

        /// <summary>
        ///     Retrieves all characteristics that match the specified UUID pattern.
        /// </summary>
        /// <param name="characteristicUUIDRegex">Regular expression pattern to match characteristic UUIDs.</param>
        /// <returns>A read-only list of matching characteristics.</returns>
        public IDeviceOperationResult GetAllCharacteristicsForUUID(string characteristicUUIDRegex)
        {
            if (Device.ConfigurationFailed) return DeviceOperation.Result<DeviceConfigurationFailedResult>();
            if (!Device.IsConfigured) return DeviceOperation.Result<DeviceNotConfiguredResult>();
            
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLECharacteristic characteristic in Characteristics)
            {
                if(Regex.IsMatch(characteristic.UUID, characteristicUUIDRegex))
                    characteristics.Add(characteristic);
            }

            return new DeviceReadSuccessful<IReadOnlyList<IBluetoothLECharacteristic>>(characteristics);
        }
    }
}