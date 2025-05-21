using System.Diagnostics;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Operations;
using IRIS.Operations.Abstract;
using IRIS.Operations.Configuration;
using IRIS.Operations.Data;
using IRIS.Operations.Generic;
using IRIS.Utility;

namespace IRIS.Bluetooth.Windows.Structure
{
    /// <summary>
    ///     Represents a Windows-specific implementation of a Bluetooth Low Energy device.
    ///     This class handles the discovery and management of BLE services and characteristics.
    /// </summary>
    internal sealed class WindowsBluetoothLEDevice : IBluetoothLEDevice
    {
        /// <summary>
        ///     The underlying Windows API Bluetooth LE device instance.
        ///     This is the native device object that provides direct access to the hardware.
        /// </summary>
        internal BluetoothLEDevice? HardwareDevice { get; }

        /// <summary>
        ///     Internal cache of discovered Bluetooth LE services.
        ///     This list is populated during device setup and contains all available services.
        /// </summary>
        private readonly List<IBluetoothLEService> _services = [];

        /// <summary>
        ///     The friendly name of the Bluetooth device as reported by the hardware.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     The unique Bluetooth address of the device.
        ///     This is a 48-bit (6-byte) address that uniquely identifies the device.
        /// </summary>
        public ulong DeviceAddress { get; }

        /// <summary>
        ///     Indicates whether the device has been fully configured and all services have been discovered.
        /// </summary>
        public bool IsConfigured { get; private set; }

        internal bool ConfigurationFailed { get; private set; }

        bool IBluetoothLEDevice.ConfigurationFailed => ConfigurationFailed;

        /// <summary>
        ///     Provides read-only access to all discovered services on the device.
        /// </summary>
        public IReadOnlyList<IBluetoothLEService> Services => _services;

        /// <summary>
        ///     Initializes a new instance of the WindowsBluetoothLEDevice class.
        /// </summary>
        /// <param name="device">The Windows Bluetooth LE device to wrap.</param>
        public WindowsBluetoothLEDevice(BluetoothLEDevice device)
        {
            HardwareDevice = device;
            DeviceAddress = device.BluetoothAddress;
            Name = device.Name;
            
            // Set-up the device
            SetupDevice().Forget();
        }

        /// <summary>
        ///     Performs the initial setup of the device by discovering and configuring all available services.
        ///     This method is called asynchronously during device initialization.
        /// </summary>
        private async ValueTask SetupDevice()
        {
            try
            {
                // Check if device is null
                if (HardwareDevice == null)
                {
                    ConfigurationFailed = true;
                    return;
                }

                // Discover services
                GattDeviceServicesResult services = await HardwareDevice.GetGattServicesAsync();

                // Check if success
                if (services.Status is not GattCommunicationStatus.Success)
                {
                    ConfigurationFailed = true;
                    return;
                }

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
            }
            catch (Exception anyException)
            {
                Debug.WriteLine(anyException);
                ConfigurationFailed = true;
            }

            DeviceConfigured?.Invoke(this);
        }

        /// <summary>
        ///     Retrieves all characteristics that match the specified UUID pattern across all services.
        /// </summary>
        /// <param name="characteristicUUIDRegex">Regular expression pattern to match characteristic UUIDs.</param>
        /// <returns>A read-only list of matching characteristics.</returns>
        public IDeviceOperationResult GetAllCharacteristicsForUUID(string characteristicUUIDRegex)
        {
            if (ConfigurationFailed) return DeviceOperation.Result<DeviceConfigurationFailedResult>();
            if (!IsConfigured) return DeviceOperation.Result<DeviceNotConfiguredResult>();
            
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLEService service in Services)
            {
                IDeviceOperationResult result = service.GetAllCharacteristicsForUUID(characteristicUUIDRegex);
                if (DeviceOperation.IsFailure(result, out IDeviceOperationResult proxyResult)) return proxyResult;
                
                if (result is IDeviceOperationResult<IReadOnlyList<IBluetoothLECharacteristic>> dataResult)
                    characteristics.AddRange(dataResult.Data);
            }

            return new DeviceDataReadSuccessfulResult<IReadOnlyList<IBluetoothLECharacteristic>>(characteristics);
        }

        /// <summary>
        ///     Retrieves all characteristics from services that match the specified service UUID pattern.
        /// </summary>
        /// <param name="serviceUUIDRegex">Regular expression pattern to match service UUIDs.</param>
        /// <returns>A read-only list of characteristics from matching services.</returns>
        public IDeviceOperationResult GetAllCharacteristicsForServices(string serviceUUIDRegex)
        {
            if (ConfigurationFailed) return DeviceOperation.Result<DeviceConfigurationFailedResult>();
            if (!IsConfigured) return DeviceOperation.Result<DeviceNotConfiguredResult>();
            
            List<IBluetoothLECharacteristic> characteristics = [];

            foreach (IBluetoothLEService service in Services)
            {
                if (Regex.IsMatch(service.UUID, serviceUUIDRegex))
                    characteristics.AddRange(service.Characteristics);
            }

            return new DeviceDataReadSuccessfulResult<IReadOnlyList<IBluetoothLECharacteristic>>(characteristics);
        }

        /// <summary>
        ///     Retrieves all characteristics from all services on the device.
        /// </summary>
        /// <returns>A read-only list of all characteristics.</returns>
        public IDeviceOperationResult GetAllCharacteristics()
        {
            if (ConfigurationFailed) return DeviceOperation.Result<DeviceConfigurationFailedResult>();
            if (!IsConfigured) return DeviceOperation.Result<DeviceNotConfiguredResult>();
            
            List<IBluetoothLECharacteristic> data =  Services.SelectMany(service => service.Characteristics).ToList();

            return new DeviceDataReadSuccessfulResult<IReadOnlyList<IBluetoothLECharacteristic>>(data);
        }

        /// <summary>
        ///     Retrieves all services that match the specified UUID pattern.
        /// </summary>
        /// <param name="serviceUUIDRegex">Regular expression pattern to match service UUIDs.</param>
        /// <returns>A read-only list of matching services.</returns>
        public IDeviceOperationResult GetAllServicesForUUID(string serviceUUIDRegex)
        {
            if (ConfigurationFailed) return DeviceOperation.Result<DeviceConfigurationFailedResult>();
            if (!IsConfigured) return DeviceOperation.Result<DeviceNotConfiguredResult>();
            
            List<IBluetoothLEService> services = [];

            foreach (IBluetoothLEService service in Services)
            {
                if (Regex.IsMatch(service.UUID, serviceUUIDRegex)) services.Add(service);
            }

            return new DeviceDataReadSuccessfulResult<IReadOnlyList<IBluetoothLEService>>(services);
        }

        /// <summary>
        ///     Event that is raised when the device has been fully configured and all services have been discovered.
        /// </summary>
        public event DeviceConfiguredHandler? DeviceConfigured;
    }
}