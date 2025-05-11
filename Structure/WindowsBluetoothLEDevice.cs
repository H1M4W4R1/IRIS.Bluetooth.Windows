using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Common.Abstract;

namespace IRIS.Bluetooth.Windows.Structure
{
    internal class WindowsBluetoothLEDevice : IBluetoothLEDevice
    {
        /// <summary>
        ///     Windows API Bluetooth LE device
        /// </summary>
        internal BluetoothLEDevice? HardwareDevice { get; }

        /// <summary>
        ///     Cache for services
        /// </summary>
        private readonly List<IBluetoothLEService> _services = [];

        /// <summary>
        ///     Name of the device
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Address of the device
        /// </summary>
        public ulong DeviceAddress { get; }

        /// <summary>
        ///     All services available on the device
        /// </summary>
        public IReadOnlyList<IBluetoothLEService> Services => _services;

        public WindowsBluetoothLEDevice(BluetoothLEDevice device)
        {
            HardwareDevice = device;
            DeviceAddress = device.BluetoothAddress;
            Name = device.Name;

            // Construct service list ;)
            IAsyncOperation<GattDeviceServicesResult>? request = device.GetGattServicesAsync();
            while (request.Status is not AsyncStatus.Completed)
            {
                // Do nothing
            }
            
            // Get result
            GattDeviceServicesResult? result = request.GetResults();
            
            // Check if success
            if (result.Status is not GattCommunicationStatus.Success) return;
            
            // Loop through services
            foreach (GattDeviceService gattService in result.Services)
            {
                _services.Add(new WindowsBluetoothLEService(this, gattService));
            }
        }

        public IReadOnlyList<IBluetoothLECharacteristic> GetAllCharacteristicsForUUID(
            string characteristicUUIDRegex)
        {
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLEService service in Services)
            {
                characteristics.AddRange(service.GetAllCharacteristicsForUUID(characteristicUUIDRegex));
            }


            return characteristics;
        }

        public IReadOnlyList<IBluetoothLECharacteristic> GetAllCharacteristicsForServices(
            string serviceUUIDRegex)
        {
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLEService service in Services)
            {
                if (Regex.IsMatch(service.UUID, serviceUUIDRegex))
                    characteristics.AddRange(service.Characteristics);
            }

            return characteristics;
        }

        public IReadOnlyList<IBluetoothLECharacteristic> GetAllCharacteristics()
        {
            return Services.SelectMany(service => service.Characteristics).ToList();
        }

        public IReadOnlyList<IBluetoothLEService> GetAllServicesForUUID(string serviceUUIDRegex)
        {
            List<IBluetoothLEService> services = [];

            foreach (IBluetoothLEService service in Services)
            {
                if (Regex.IsMatch(service.UUID, serviceUUIDRegex)) services.Add(service);
            }


            return services;
        }
    }
}