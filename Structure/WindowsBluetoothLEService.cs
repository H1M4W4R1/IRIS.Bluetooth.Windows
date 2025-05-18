using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Common.Abstract;

namespace IRIS.Bluetooth.Windows.Structure
{
    internal sealed class WindowsBluetoothLEService(IBluetoothLEDevice device, GattDeviceService service)
        : IBluetoothLEService
    {
        private readonly List<IBluetoothLECharacteristic> _characteristics = new();

        /// <summary>
        ///     GATT service that this service is associated with
        /// </summary>
        internal GattDeviceService? GattService { get; } = service;

        /// <summary>
        ///     Device that this service is associated with
        /// </summary>
        public IBluetoothLEDevice Device { get; } = device;

        /// <summary>
        ///     UUID of the service
        /// </summary>
        public string UUID { get; } = service.Uuid.ToString();

        public IReadOnlyList<IBluetoothLECharacteristic> Characteristics => _characteristics;

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

        public IReadOnlyList<IBluetoothLECharacteristic> GetAllCharacteristicsForUUID(
            string characteristicUUIDRegex)
        {
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLECharacteristic characteristic in Characteristics)
            {
                if(Regex.IsMatch(characteristic.UUID, characteristicUUIDRegex))
                    characteristics.Add(characteristic);
            }

            return characteristics;
        }
    }
}