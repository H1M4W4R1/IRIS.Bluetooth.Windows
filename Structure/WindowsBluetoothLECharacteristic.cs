using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IRIS.Bluetooth.Windows.Structure
{
    /// <summary>
    /// Represents a Bluetooth Low Energy characteristic in the Windows environment.
    /// This class provides functionality to interact with BLE characteristics including reading,
    /// writing, and subscribing to notifications.
    /// </summary>
    internal sealed class WindowsBluetoothLECharacteristic(IBluetoothLEService service,
        GattCharacteristic characteristic
    )
        : IBluetoothLECharacteristic
    {
        /// <summary>
        /// The underlying GATT characteristic that this wrapper is associated with.
        /// </summary>
        internal GattCharacteristic? GattCharacteristic { get; init; } = characteristic;

        /// <summary>
        /// Gets the Bluetooth LE service that this characteristic belongs to.
        /// </summary>
        public IBluetoothLEService Service { get; } = service;

        /// <summary>
        /// Gets the unique identifier (UUID) of this characteristic.
        /// </summary>
        public string UUID { get; } = characteristic.Uuid.ToString();

        /// <summary>
        /// Indicates whether this characteristic supports read operations.
        /// </summary>
        public bool IsRead
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read) == true;

        /// <summary>
        /// Indicates whether this characteristic supports write operations (including write without response).
        /// </summary>
        public bool IsWrite
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse) == true;

        /// <summary>
        /// Indicates whether this characteristic supports notifications.
        /// </summary>
        public bool IsNotify
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) == true;

        /// <summary>
        /// Event that is raised when the characteristic's value changes.
        /// </summary>
        public event CharacteristicValueChangedHandler? ValueChanged;

        /// <summary>
        /// Indicates whether the characteristic is available for operations.
        /// When true, the GattCharacteristic property is guaranteed to be non-null.
        /// </summary>
        [MemberNotNullWhen(true, nameof(GattCharacteristic))] 
        public bool IsAvailable
            => GattCharacteristic is not null;

        /// <summary>
        /// Reads the current value of the characteristic.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The characteristic's value as a byte array, or null if the read operation fails.</returns>
        internal async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return null;

                GattReadResult result = await GattCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);
                if (result.Status != GattCommunicationStatus.Success) return null;

                byte[] data = new byte[result.Value.Length];
                using DataReader reader = DataReader.FromBuffer(result.Value);
                reader.ReadBytes(data);

                return data;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return null;
            }
        }

        /// <summary>
        /// Explicit interface implementation for writing to the characteristic.
        /// </summary>
        ValueTask<bool> IBluetoothLECharacteristic.WriteAsync(byte[] data, CancellationToken cancellationToken) =>
            WriteAsync(data, cancellationToken);

        /// <summary>
        /// Explicit interface implementation for reading from the characteristic.
        /// </summary>
        ValueTask<byte[]?> IBluetoothLECharacteristic.ReadAsync(CancellationToken cancellationToken) =>
            ReadAsync(cancellationToken);

        /// <summary>
        /// Writes data to the characteristic.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the write operation was successful, false otherwise.</returns>
        internal async ValueTask<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                using DataWriter writer = new();
                writer.WriteBytes(data);
                GattCommunicationStatus status = await GattCharacteristic
                    .WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse)
                    .AsTask(cancellationToken);

                return status == GattCommunicationStatus.Success;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        /// <summary>
        /// Subscribes to notifications from this characteristic.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the subscription was successful, false otherwise.</returns>
        public async ValueTask<bool> SubscribeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                GattCommunicationStatus status = await GattCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(cancellationToken);

                if (status != GattCommunicationStatus.Success) return false;

                GattCharacteristic.ValueChanged += OnValueChanged;
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        /// <summary>
        /// Unsubscribes from notifications from this characteristic.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the unsubscription was successful, false otherwise.</returns>
        public async ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                GattCommunicationStatus status = await GattCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask(cancellationToken);

                if (status != GattCommunicationStatus.Success) return false;

                GattCharacteristic.ValueChanged -= OnValueChanged;
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        /// <summary>
        /// Handles value change notifications from the characteristic.
        /// </summary>
        /// <param name="sender">The characteristic that sent the notification.</param>
        /// <param name="args">The event arguments containing the new value.</param>
        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data = new byte[args.CharacteristicValue.Length];
            using DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
            reader.ReadBytes(data);

            ValueChanged?.Invoke(this, data);
        }
    }
}