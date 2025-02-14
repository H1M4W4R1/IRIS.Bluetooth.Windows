using System.Runtime.CompilerServices;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using IRIS.Bluetooth.Addressing;
using IRIS.Bluetooth.Utility;

namespace IRIS.Bluetooth.Communication
{
    /// <summary>
    /// Represents bluetooth endpoint (service) we can connect to
    /// </summary>
    public sealed class BLE_Endpoint(BluetoothLEInterface bluetoothInterface,
        GattDeviceService service,
        GattCharacteristic characteristic
    )
    {
        public delegate void NotificationReceivedHandler(
            GattCharacteristic sender,
            GattValueChangedEventArgs args);

        /// <summary>
        /// List of allowed service addresses
        /// </summary>
        public BluetoothLEServiceAddress ServiceAddress { get; } = new(service.Uuid);

        /// <summary>
        /// Interface to communicate with the device
        /// </summary>
        public BluetoothLEInterface Interface { get; } = bluetoothInterface;

        /// <summary>
        /// UUID of the characteristic
        /// </summary>
        public GattCharacteristic Characteristic { get; init; } = characteristic;

        /// <summary>
        /// GATT service on the device
        /// </summary>
        public GattDeviceService Service { get; } = service;

        /// <summary>
        /// Check if notifications are active
        /// </summary>
        public bool AreNotificationsActive { get; private set; }

        /// <summary>
        /// Check if notifications can be set
        /// </summary>
        public bool IsNotifyAvailable
            => Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify);

        /// <summary>
        /// Called when notification is received (must be set beforehand)
        /// </summary>
        public event NotificationReceivedHandler? NotificationReceived = delegate { };

        /// <summary>
        /// Read value from the characteristic
        /// </summary>
        private async ValueTask<IBuffer?> ReadRawValue()
        {
            GattReadResult result = await Characteristic.ReadValueAsync();
            return result.Status != GattCommunicationStatus.Success ? null : result.Value;
        }

        /// <summary>
        /// Write value to the characteristic
        /// </summary>
        private async ValueTask<bool> WriteRawValue(IBuffer buffer)
        {
            GattCommunicationStatus status = await Characteristic.WriteValueAsync(buffer);
            return status == GattCommunicationStatus.Success;
        }

        public async ValueTask<(bool, TObjectType?)> WriteWithResponse<TObjectType>()
        {
            IBuffer? buffer = await ReadRawValue();
            if (buffer == null) return (false, default);
            return buffer.Read<TObjectType>();
        }

        /// <summary>
        /// Write data to the characteristic
        /// </summary>
        /// <returns>True if write was successful, false otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<bool> Write<TObjectType>(TObjectType data)
        {
            // Convert data to buffer
            IBuffer? buffer = data.ToBuffer();
            if (buffer == null) return false;

            // Write data
            return await WriteRawValue(buffer);
        }

        /// <summary>
        /// Read data from the characteristic
        /// </summary>
        /// <returns>Value of the characteristic or null if type is not supported or read failed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public async ValueTask<TObjectType?> ReadData<TObjectType>()
        {
            // Read raw value
            IBuffer? buffer = await ReadRawValue();
            if (buffer == null) return default;

            // Read data
            (bool success, TObjectType? data) = buffer.Read<TObjectType>();
            return success ? data : default;
        }

        /// <summary>
        /// Set notify for this characteristic
        /// </summary>
        public async ValueTask<bool> SetNotify(bool shallNotify)
        {
            // Check if service supports notify
            if (!Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                return false;

            // Set notify
            GattCommunicationStatus status =
                await Characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    shallNotify
                        ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                        : GattClientCharacteristicConfigurationDescriptorValue.None);

            // Check if status is OK
            if (status != GattCommunicationStatus.Success) return false;

            // Set notification handler
            if (shallNotify)
                Characteristic.ValueChanged += OnNotificationReceivedHandler;
            else
                Characteristic.ValueChanged -= OnNotificationReceivedHandler;

            AreNotificationsActive = shallNotify;
            return true;
        }

        private void OnNotificationReceivedHandler(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // Notify all listeners
            NotificationReceived?.Invoke(sender, args);
        }
    }
}