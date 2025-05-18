using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Utility;

namespace IRIS.Bluetooth.Windows.Structure
{
    internal sealed class WindowsBluetoothLEDevice : IBluetoothLEDevice
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

        public bool IsConfigured { get; private set; }

        /// <summary>
        ///     All services available on the device
        /// </summary>
        public IReadOnlyList<IBluetoothLEService> Services => _services;

        public WindowsBluetoothLEDevice(BluetoothLEDevice device)
        {
            HardwareDevice = device;
            DeviceAddress = device.BluetoothAddress;
            Name = device.Name;
            
            // Set-up the device
            SetupDevice().Forget();
        }

        private async ValueTask SetupDevice()
        {
            // Check if device is null
            if (HardwareDevice == null) return;
            
            // Discover services
            GattDeviceServicesResult services = await HardwareDevice.GetGattServicesAsync();
            
            // Check if success
            if (services.Status is not GattCommunicationStatus.Success) return;
            
            // Register services
            foreach (GattDeviceService gattService in services.Services)
            {
                // Create new service
                WindowsBluetoothLEService service = new WindowsBluetoothLEService(this, gattService);
                
                // Setup service
                await service.SetupService();
                
                // Add to list
                _services.Add(service);
            }
            
            IsConfigured = true;
            DeviceConfigured?.Invoke(this);
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

        public event DeviceConfiguredHandler? DeviceConfigured;
    }
}