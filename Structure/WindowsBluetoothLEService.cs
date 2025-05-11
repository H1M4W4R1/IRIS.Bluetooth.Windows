using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Common.Abstract;

namespace IRIS.Bluetooth.Windows.Structure
{
    internal sealed class WindowsBluetoothLEService : IBluetoothLEService
    {
        private readonly List<IBluetoothLECharacteristic> _characteristics = new();

        /// <summary>
        ///     GATT service that this service is associated with
        /// </summary>
        internal GattDeviceService? GattService { get; }

        /// <summary>
        ///     Device that this service is associated with
        /// </summary>
        public IBluetoothLEDevice Device { get; }

        /// <summary>
        ///     UUID of the service
        /// </summary>
        public string UUID { get; }

        public IReadOnlyList<IBluetoothLECharacteristic> Characteristics => _characteristics;

        public WindowsBluetoothLEService(IBluetoothLEDevice device, GattDeviceService service)
        {
            Device = device;
            UUID = service.Uuid.ToString();
            GattService = service;

            // Construct characteristic list
            IAsyncOperation<GattCharacteristicsResult>? asyncOp = service.GetCharacteristicsAsync();

            while (asyncOp.Status is not AsyncStatus.Completed) ;

            // Get async operation results
            GattCharacteristicsResult? result = asyncOp.GetResults();

            // Check if status is success
            if (result.Status is not GattCommunicationStatus.Success) return;
            
            foreach (GattCharacteristic gattCharacteristic in result.Characteristics)
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